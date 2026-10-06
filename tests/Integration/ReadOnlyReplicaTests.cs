using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Repositories;
using Xunit;

namespace Integration;

public class ReadOnlyReplicaTests
{
    private const string BaseConnectionString = "Server=localhost,1433;Database=PixTestDb;User Id=sa;Password=YourStrong@Passw0rd!;TrustServerCertificate=True;";

    [Fact]
    public async Task REPL_016_Should_Direcionar_Consulta_Para_Replica_De_Leitura_When_ApplicationIntent_E_ReadOnly()
    {
        // Given: Connection string configurada explicitamente com ApplicationIntent=ReadOnly
        var readOnlyConnString = $"{BaseConnectionString}ApplicationIntent=ReadOnly;";
        var repo = new ReadOnlyReplicaRepository(readOnlyConnString);

        // Then 1: String de conexão deve conter a diretiva ApplicationIntent=ReadOnly
        repo.ConnectionString.Should().Contain("ApplicationIntent=ReadOnly");

        // When: Consulta de saldo executada contra a réplica
        var contaId = new ContaId("cnt-replica-1");
        var saldo = await repo.ObterSaldoAsync(contaId);

        // Then 2: Retorna estrutura de saldo válida
        saldo.Should().NotBeNull();
        saldo!.ContaId.Should().Be(contaId);
    }

    [Fact]
    public async Task REPL_017_Should_Falhar_Escrita_When_Executada_Acidentalmente_Contra_Replica_De_Leitura()
    {
        // Given: Conexão aberta com ApplicationIntent=ReadOnly
        var readOnlyConnString = $"{BaseConnectionString}ApplicationIntent=ReadOnly;";
        var repo = new ReadOnlyReplicaRepository(readOnlyConnString);

        // When: Tentar executar comando de mutação (INSERT)
        Func<Task> act = async () => await repo.ExecutarMutacaoNaoPermitidaAsync("INSERT INTO [pix].[Pedidos] (Id) VALUES (NEWID())");

        // Then: Deve ser bloqueado, rejeitando mutações na réplica
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*proibidas na réplica de leitura*");
    }

    [Fact]
    public async Task REPL_RW_003_Should_Consultar_Saldo_Real_No_Banco_De_Dados_Via_Replica()
    {
        // Given: Banco migrado com schema e tabela de saldos [pix].[Saldos]
        DbUpRunner.Migrar(BaseConnectionString);

        await using (var conn = new SqlConnection(BaseConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"
                IF NOT EXISTS (SELECT 1 FROM [pix].[Saldos] WHERE [ContaId] = 'conta-spb-001')
                BEGIN
                    INSERT INTO [pix].[Saldos] ([ContaId], [SaldoDisponivel], [LimiteDiario], [Moeda], [AtualizadoEm])
                    VALUES ('conta-spb-001', 15000.00, 50000.00, 'BRL', SYSDATETIMEOFFSET());
                END");
        }

        var repo = new ReadOnlyReplicaRepository($"{BaseConnectionString}ApplicationIntent=ReadOnly;");
        var contaId = new ContaId("conta-spb-001");

        // When: Consulta real executada via Dapper na réplica
        var saldo = await repo.ObterSaldoAsync(contaId);

        // Then: Retorna valores reais persistidos no banco de dados
        saldo.Should().NotBeNull();
        saldo!.ContaId.Should().Be(contaId);
        saldo.SaldoDisponivel.Should().Be(15000.00m);
        saldo.LimiteCredito.Should().Be(50000.00m);
        saldo.Moeda.Should().Be("BRL");
    }

    [Fact]
    public async Task QA_003_Should_Retornar_Saldo_Zero_Sem_Fabricacao_Sintetica_When_Conta_Inexistente_Ou_Replica_Falha()
    {
        // Given: Repositório de réplica apontando para conexão
        var repo = new ReadOnlyReplicaRepository($"{BaseConnectionString}ApplicationIntent=ReadOnly;");
        var contaInexistente = new ContaId("conta-inexistente-12345");

        // When: Consulta de saldo para conta que não existe em [pix].[Saldos]
        var saldo = await repo.ObterSaldoAsync(contaInexistente);

        // Then: Saldo deve ser estritamente zero sem fabricação sintética arbitrária de R$ 5.000,00
        saldo.Should().NotBeNull();
        saldo!.ContaId.Should().Be(contaInexistente);
        saldo.SaldoDisponivel.Should().Be(0.00m);
        saldo.LimiteCredito.Should().Be(0.00m);
        saldo.Moeda.Should().Be("BRL");
    }
}
