using FluentAssertions;
using Pix.Processing.Application.Features.FallbackReadBalance;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;
using Pix.Processing.Infrastructure.Repositories;
using Xunit;

namespace Integration;

public class FallbackCacheAndResilienceTests
{
    private const string BaseConnectionString = "Server=localhost,1433;Database=PixTestDb;User Id=sa;Password=YourStrong@Passw0rd!;TrustServerCertificate=True;";

    public FallbackCacheAndResilienceTests()
    {
        DbUpRunner.Migrar(BaseConnectionString);
        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(BaseConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                IF EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'pix' AND t.name = 'Saldos')
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM [pix].[Saldos] WHERE [ContaId] = 'cnt-cache-fallback-1')
                    BEGIN
                        INSERT INTO [pix].[Saldos] ([ContaId], [SaldoDisponivel], [LimiteDiario], [Moeda], [AtualizadoEm])
                        VALUES ('cnt-cache-fallback-1', 5000.00, 10000.00, 'BRL', SYSDATETIMEOFFSET());
                    END;
                END;";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    [Fact]
    public async Task RES_018_Should_Consultar_Replica_Sincrona_Com_IMemoryCache_When_Redis_Esta_Indisponivel()
    {
        // Given: Réplica de leitura e cache local configurado
        var replicaRepo = new ReadOnlyReplicaRepository($"{BaseConnectionString}ApplicationIntent=ReadOnly;");
        var localCache = new LocalMemoryCacheAdapter();
        var useCase = new FallbackReadBalanceUseCase(replicaRepo, localCache);

        var contaId = new ContaId("cnt-cache-fallback-1");

        // When 1: Primeira chamada (cache miss -> lê da réplica e popula cache por 30s)
        var saldo1 = await useCase.ConsultarSaldoAsync(contaId);

        // Then 1: Retorna saldo com sucesso
        saldo1.Should().NotBeNull();
        saldo1!.ContaId.Should().Be(contaId);
        saldo1.SaldoDisponivel.Should().BeGreaterThan(0);

        // Then 2: Cache local deve conter o saldo armazenado
        localCache.TryGetValue(contaId.Valor, out SaldoInfo? saldoCache).Should().BeTrue();
        saldoCache.Should().BeEquivalentTo(saldo1);

        // When 2: Segunda chamada deve ser servida diretamente pelo cache
        var saldo2 = await useCase.ConsultarSaldoAsync(contaId);
        saldo2.Should().BeEquivalentTo(saldo1);
    }

    [Fact]
    public async Task RES_010_Should_Aplicar_Circuit_Breaker_When_Admissao_Detecta_Redis_Em_Manutencao()
    {
        // Given: Adaptador resiliente de cache com limite de 3 falhas para abrir circuito
        var replicaRepo = new ReadOnlyReplicaRepository($"{BaseConnectionString}ApplicationIntent=ReadOnly;");
        var localCache = new LocalMemoryCacheAdapter();
        var adapter = new ResilientCacheAdapter(replicaRepo, localCache, failureThreshold: 3, openDuration: TimeSpan.FromSeconds(5));

        // When: Injetar falhas simuladas de conexão com o Redis
        adapter.SimularFalhaRedis(true);

        for (int i = 0; i < 3; i++)
        {
            var res = await adapter.TentarOperacaoComProtecaoAsync();
            res.Sucesso.Should().BeFalse();
        }

        // Then 1: Circuito deve transicionar para estado Aberto (Open)
        adapter.EstadoCircuito.Should().Be(CircuitBreakerState.Open);

        // Then 2: Novas requisições de admissão sob circuito aberto são barradas com status 429/503 controlado
        var resBloqueado = await adapter.TentarOperacaoComProtecaoAsync();
        resBloqueado.CircuitoAberto.Should().BeTrue("Circuit breaker deve barrar requisições para proteger o sistema");
        resBloqueado.StatusCode.Should().Be(503);
    }

    [Fact]
    public void RES_RW_001_Should_Respeitar_Capacidade_Maxima_E_Expurgar_Excedentes_Prevenindo_OOM()
    {
        // Given: Cache local configurado com teto restrito de 5 itens para evitar OOM
        var boundedCache = new LocalMemoryCacheAdapter(capacidadeMaxima: 5);

        // When: Inserir 10 chaves diferentes durante janela de indisponibilidade
        for (int i = 1; i <= 10; i++)
        {
            boundedCache.Set($"conta-key-{i}", $"saldo-{i}", TimeSpan.FromMinutes(1));
        }

        // Then: O número de itens retidos no cache não pode ultrapassar o teto bounded configurado
        boundedCache.Count.Should().BeLessOrEqualTo(5, "Cache em memória deve ter limite estrito para evitar OOM sob alta cardinalidade");
        boundedCache.CapacidadeMaxima.Should().Be(5);
    }
}

