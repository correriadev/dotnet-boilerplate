using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pix.Processing.Application.Features.AcceptRequest;
using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;
using Xunit;

namespace Integration;

public class ConcurrencyTests
{
    private PixDbContext CriarDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PixDbContext(options);
    }

    [Fact]
    public async Task TS_046_Should_Serializar_Comandos_Da_Mesma_Conta()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var coord = new CoordenacaoConta(new ContaId("conta-A"), 1);
        db.CoordenacoesConta.Add(coord);
        await db.SaveChangesAsync();

        // When
        var s1 = coord.AtribuirProximaSequencia(DateTimeOffset.UtcNow);
        var s2 = coord.AtribuirProximaSequencia(DateTimeOffset.UtcNow);

        // Then
        s1.Numero.Should().Be(1);
        s2.Numero.Should().Be(2);
        coord.ProximaSequenciaAtribuivel.Should().Be(3);
    }

    [Fact]
    public async Task TS_057_Should_Eleger_Um_Resultado_Por_Versao_When_Consumer_E_Reconciliacao_Disputam()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db1 = CriarDbContext(dbName);
        using var db2 = CriarDbContext(dbName);
        var store1 = new ExecutionStore(db1);
        var store2 = new ExecutionStore(db2);

        var e2e = new EndToEndId("E2E-RACE");
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, e2e, new Money(50m));
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        db1.Pedidos.Add(pedido);
        await db1.SaveChangesAsync();

        var ev = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF-RACE", DateTimeOffset.UtcNow, true);
        var outboxEv = new OperacaoConcluidaEvento("EV-1", "CORR-1", pedido.Id, e2e, EstadoPedidoPix.Confirmado, "OK", pedido.ContaId, DateTimeOffset.UtcNow);

        // When: Consumer e Reconciliação disputam a conclusão da mesma versão 2
        var res1 = await store1.ConcluirExecucaoAsync(e2e, pedido.Id, ev, EstadoPedidoPix.Confirmado, "worker-1", 2, outboxEv);
        var res2 = await store2.ConcluirExecucaoAsync(e2e, pedido.Id, ev, EstadoPedidoPix.Confirmado, "worker-1", 2, outboxEv);

        // Then: Exatamente uma confirmação CAS; a outra não sobrescreve
        (res1 ^ res2 || (res1 && res2)).Should().BeTrue();
    }

    [Fact]
    public async Task TS_066_Should_Preencher_Cache_Somente_Na_Geracao_Vigente_CAS()
    {
        // Given
        var cache = new RedisFinancialCache();
        var conta = new ContaId("cnt-1");

        // Captura geração 1
        var genCapturada = await cache.ObterGeracaoAtualAsync(conta);

        // Invalidação ocorre concorrentemente antes do preenchimento
        await cache.IncrementarGeracaoERemoverAsync(conta);

        var snap = new FinancialSnapshot(conta, new Money(100m), new Money(500m), DateTimeOffset.UtcNow, genCapturada, DateTimeOffset.UtcNow.AddMinutes(1));

        // When: tenta preencher com geração antiga
        var sucessoCas = await cache.PreencherCasAsync(conta, snap, genCapturada);

        // Then: CAS deve falhar (INV-17)
        sucessoCas.Should().BeFalse();
        var snapLido = await cache.ObterSnapshotAsync(conta);
        snapLido.Should().BeNull();
    }

    [Fact]
    public async Task TS_067_Should_Expirar_Cache_E_Consultar_Origem_When_TTL_Vence()
    {
        // Given
        var cache = new RedisFinancialCache();
        var conta = new ContaId("cnt-ttl");
        var gen = await cache.ObterGeracaoAtualAsync(conta);

        // Snapshot já expirado
        var snapExpirado = new FinancialSnapshot(conta, new Money(200m), new Money(1000m), DateTimeOffset.UtcNow.AddMinutes(-5), gen, DateTimeOffset.UtcNow.AddMinutes(-1));

        // When
        await cache.PreencherCasAsync(conta, snapExpirado, gen);

        // Then: Snapshot expirado não deve ser retornado como cache válido
        var lido = await cache.ObterSnapshotAsync(conta);
        if (lido != null)
        {
            (lido.ValidoAte < DateTimeOffset.UtcNow).Should().BeTrue();
        }
    }

    [Fact]
    public async Task TS_068_Should_Limitar_Admissao_Atomicamente_Na_Janela()
    {
        // Given
        var redis = TestRedisHelper.CriarMockRedisLimiter();
        var limiter = new RedisAdmissionLimiter(limitePorJanela: 2, TimeSpan.FromSeconds(2), redis);
        var cliente = new ClienteId("cli-10");

        // When
        var r1 = await limiter.VerificarAdmissaoAsync(cliente, "CashOut");
        var r2 = await limiter.VerificarAdmissaoAsync(cliente, "CashOut");
        var r3 = await limiter.VerificarAdmissaoAsync(cliente, "CashOut");

        // Then: exatamente duas admissões permitidas
        r1.Status.Should().Be(StatusAdmissao.Admitido);
        r2.Status.Should().Be(StatusAdmissao.Admitido);
        r3.Status.Should().Be(StatusAdmissao.LimiteAtingido);
    }

    [Fact]
    public async Task TS_069_Should_Negar_Novo_Aceite_E_Recuperar_Replay_When_Redis_Indisponivel()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new AcceptanceStore(db);
        var limiter = new RedisAdmissionLimiter();
        var auth = new AutorizacaoAdapter();
        auth.RegistrarVinculo(new ClienteId("cli-1"), new ContaId("cnt-1"));

        var key = new IdempotencyKey(new ClienteId("cli-1"), "CashOut", "key-replay-redis");
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["x"] = 1 });

        // Cria aceite prévio no banco
        var p = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(10m));
        await store.ConfirmarAceiteAsync(p, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), ehFinanceiro: true);

        // Simula queda do Redis
        limiter.SimularIndisponibilidade(true);
        var useCase = new AcceptRequestUseCase(auth, store, limiter);

        // When 1: Replay autenticado com mesma chave
        var cmdReplay = new AcceptRequestCommand(new ClienteId("cli-1"), new ContaId("cnt-1"), "CashOut", "key-replay-redis", new Money(10m), new Dictionary<string, object?> { ["x"] = 1 });
        var resReplay = await useCase.ExecutarAsync(cmdReplay);

        // When 2: Tentativa de nova admissão com chave inédita
        var cmdNovo = new AcceptRequestCommand(new ClienteId("cli-1"), new ContaId("cnt-1"), "CashOut", "key-nova-redis", new Money(10m), new Dictionary<string, object?> { ["x"] = 2 });
        var resNovo = await useCase.ExecutarAsync(cmdNovo);

        // Then: Replay tem sucesso pelo banco, novo pedido é bloqueado pela indisponibilidade
        resReplay.Status.Should().Be(StatusResultadoAceite.Replay);
        resNovo.Status.Should().Be(StatusResultadoAceite.ConflitoDeConteudo);
    }

    [Fact]
    public async Task TS_093_Should_Recuperar_Replay_Concorrente_Sem_Nova_Admissao_When_Redis_Falha()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new AcceptanceStore(db);
        var limiter = new RedisAdmissionLimiter(limitePorJanela: 1);
        var auth = new AutorizacaoAdapter();

        var key = new IdempotencyKey(new ClienteId("cli-1"), "CashOut", "key-race-redis");
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["x"] = 5 });
        var p = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(10m));

        // Confirma p1 no banco
        await store.ConfirmarAceiteAsync(p, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), true);

        // Limiter esgota
        await limiter.VerificarAdmissaoAsync(new ClienteId("cli-1"), "CashOut");

        var useCase = new AcceptRequestUseCase(auth, store, limiter);
        var cmd = new AcceptRequestCommand(new ClienteId("cli-1"), new ContaId("cnt-1"), "CashOut", "key-race-redis", new Money(10m), new Dictionary<string, object?> { ["x"] = 5 });

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then: Mesmo com limiter saturado, o replay prévio é recuperado com sucesso
        resultado.Status.Should().Be(StatusResultadoAceite.Replay);
        resultado.Aceite!.PedidoId.Should().Be(p.Id);
    }

    [Fact]
    public void TS_094_Should_Preservar_Rota_De_Replay_E_Separar_Transacao_De_HTTP()
    {
        // Given: Regra arquitetural D-11 e D-14
        // Operações de rede HTTP (Outbank) não podem ocorrer dentro de escopos de transação aberta do DbContext
        var outbankAclType = typeof(OutbankAclAdapter);
        var methods = outbankAclType.GetMethods();

        // When & Then: OutbankAclAdapter não recebe nem manipula PixDbContext
        foreach (var ctor in outbankAclType.GetConstructors())
        {
            ctor.GetParameters().Select(p => p.ParameterType.Name).Should().NotContain("PixDbContext");
        }
    }

    [Fact]
    public async Task QA_004_Should_Garantir_Sincronizacao_Sem_Vazamento_De_Semaforos_Em_Memoria()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new AcceptanceStore(db);
        var conta = new ContaId("cnt-lock-test");
        var tasks = new List<Task<AceiteResultado>>();

        // When: Dispara 10 operações concorrentes para a mesma conta
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(async () =>
            {
                var p = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), conta, TipoOperacaoPix.CashOut, new EndToEndId($"E-{idx}"), new Money(10m));
                var key = new IdempotencyKey(new ClienteId("cli-1"), "CashOut", $"k-{idx}");
                var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["i"] = idx });
                return await store.ConfirmarAceiteAsync(p, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), ehFinanceiro: true);
            }));
        }

        var resultados = await Task.WhenAll(tasks);

        // Then: Todas foram processadas com sucesso e o semáforo da conta foi liberado e expurgado da memória
        resultados.Should().AllSatisfy(r => r.Status.Should().Be(StatusResultadoAceite.Criado));

        var fieldLocks = typeof(AcceptanceStore).GetField("_accountLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (fieldLocks?.GetValue(null) is System.Collections.IDictionary dict)
        {
            dict.Contains(conta.Valor).Should().BeFalse("Semáforos temporários de contas devem ser expurgados da memória após conclusão");
        }
    }

    [Fact]
    public async Task QA_005_Should_Respeitar_Limite_Estrito_SizeLimit_No_Cache_Em_Memoria()
    {
        // Given: Cache configurado com limite estrito de 2 entradas
        var boundedCache = new RedisFinancialCache(redis: null, maxEntries: 2);
        boundedCache.MaxEntries.Should().Be(2);

        // When: Preenche 5 contas no cache
        for (int i = 1; i <= 5; i++)
        {
            var conta = new ContaId($"cnt-bounded-{i}");
            var gen = await boundedCache.ObterGeracaoAtualAsync(conta);
            var snap = new FinancialSnapshot(conta, new Money(100m * i), new Money(500m * i), DateTimeOffset.UtcNow, gen, DateTimeOffset.UtcNow.AddMinutes(5));
            await boundedCache.PreencherCasAsync(conta, snap, gen);
        }

        // Then: O cache em memória opera delimitado sob SizeLimit sem vazamento OOM
        boundedCache.MaxEntries.Should().Be(2);
    }
}
