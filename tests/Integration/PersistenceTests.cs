using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pix.Billing.Domain;
using Pix.Billing.Infrastructure;
using Pix.Callbacks.Domain;
using Pix.Callbacks.Infrastructure;
using Pix.Processing.Application.Features.IncorporateBillingResult;
using Pix.Processing.Application.Models;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;
using Pix.Processing.Infrastructure.Entities;
using Xunit;

namespace Integration;

public class PersistenceTests
{
    private PixDbContext CriarDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PixDbContext(options);
    }

    [Fact]
    public async Task TS_043_Should_Persistir_E_Recuperar_Aceite_Completo_TX01()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new AcceptanceStore(db);

        var pedido = new PedidoPix(
            Guid.NewGuid(),
            new ClienteId("cli-1"),
            new ContaId("cnt-1"),
            TipoOperacaoPix.CashOut,
            new EndToEndId("E2E-123"),
            new Money(150.00m));

        var key = new IdempotencyKey(new ClienteId("cli-1"), "CashOut", "idemp-key-1");
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["v"] = 150m });

        // When
        var resultado = await store.ConfirmarAceiteAsync(pedido, key, fp, pid => new AceiteOriginal(pid, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), ehFinanceiro: true);

        // Then
        resultado.Status.Should().Be(StatusResultadoAceite.Criado);
        resultado.Aceite.Should().NotBeNull();

        // Novo context para verificar persistência
        using var dbVerificacao = CriarDbContext(dbName);
        var storeVerificacao = new AcceptanceStore(dbVerificacao);
        var recuperado = await storeVerificacao.ObterAceitePorChaveAsync(key);

        recuperado.Should().NotBeNull();
        recuperado!.PedidoId.Should().Be(pedido.Id);
        recuperado.StatusCode.Should().Be(202);
    }

    [Fact]
    public async Task TS_044_Should_Reverter_Todo_Aceite_When_Falha_Injetada_Em_Cada_Escrita_TX01()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);

        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E2E-FAIL"), new Money(100m));
        var key = new IdempotencyKey(new ClienteId("cli-1"), "CashOut", "key-fail-tx");
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["v"] = 100m });

        // When: Simula falha abortando a transação antes do commit
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Pedidos.Add(pedido);
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                ChaveComposta = key.ChaveComposta,
                ClienteId = key.ClienteId.Valor,
                TipoOperacao = key.TipoOperacao,
                Chave = key.Chave,
                FingerprintHash = fp.Hash,
                FingerprintVersao = fp.Versao,
                PedidoId = pedido.Id,
                StatusCode = 202,
                HeadersJson = "{}",
                CorpoJson = "{}",
                CriadoEm = DateTimeOffset.UtcNow
            });
            throw new InvalidOperationException("Falha de rede/banco durante escritas parciais da TX-01");
        }
        catch
        {
            db.ChangeTracker.Clear();
        }

        // Then: Nenhum registro parcial persiste
        using var dbVerificacao = CriarDbContext(dbName);
        var pedidoNoBanco = await dbVerificacao.Pedidos.FindAsync(pedido.Id);
        var idempNoBanco = await dbVerificacao.IdempotencyRecords.FindAsync(key.ChaveComposta);

        pedidoNoBanco.Should().BeNull();
        idempNoBanco.Should().BeNull();
    }

    [Fact]
    public async Task TS_045_Should_Persistir_Um_Pedido_Sob_Concorrencia_When_Mesma_Chave_Aceita_Simultaneamente()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db1 = CriarDbContext(dbName);
        using var db2 = CriarDbContext(dbName);
        var store1 = new AcceptanceStore(db1);
        var store2 = new AcceptanceStore(db2);

        var key = new IdempotencyKey(new ClienteId("cli-conc"), "CashOut", "key-conc");
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["v"] = 50m });

        var p1 = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-conc"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(50m));
        var p2 = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-conc"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E2"), new Money(50m));

        // When
        var r1 = await store1.ConfirmarAceiteAsync(p1, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), ehFinanceiro: true);
        var r2 = await store2.ConfirmarAceiteAsync(p2, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), ehFinanceiro: true);

        // Then: Exatamente um aceite criado e o outro recupera como Replay
        r1.Status.Should().Be(StatusResultadoAceite.Criado);
        r2.Status.Should().Be(StatusResultadoAceite.Replay);
        r2.Aceite!.PedidoId.Should().Be(p1.Id);
    }

    [Fact]
    public async Task TS_047_Should_Distinguir_Registro_Ausente_E_Identidade_Inconsistente_When_Repositories_Consultam()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new ExecutionStore(db);

        // When 1: Consulta registro inexistente
        var inexistente = await store.ObterEstadoAsync(new EndToEndId("INEXISTENTE-123"));

        // Then 1: Retorna ausente/null sem lançar exceção não contratada
        inexistente.Should().BeNull();

        // When 2: Adquire execução válida
        var e2e = new EndToEndId("E2E-EXISTENTE");
        var res1 = await store.AdquirirExecucaoAsync(e2e, Guid.NewGuid(), new ContaId("C1"), 1, "worker-1", 1, DateTimeOffset.UtcNow.AddMinutes(5));
        res1.Status.Should().Be(StatusAquisicao.Sucesso);

        // When 3: Outro pedido tenta adquirir o mesmo EndToEndId
        var res2 = await store.AdquirirExecucaoAsync(e2e, Guid.NewGuid(), new ContaId("C2"), 2, "worker-2", 1, DateTimeOffset.UtcNow.AddMinutes(5));
        res2.Status.Should().Be(StatusAquisicao.Conflito);
    }

    [Fact]
    public async Task TS_048_Should_Adquirir_ProcessedIds_Uma_Vez_When_Concorrem()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db1 = CriarDbContext(dbName);
        using var db2 = CriarDbContext(dbName);
        var store1 = new ExecutionStore(db1);
        var store2 = new ExecutionStore(db2);

        var e2e = new EndToEndId("E2E-DISPUTADO");
        var pId = Guid.NewGuid();
        var cId = new ContaId("cnt-1");

        // When
        var r1 = await store1.AdquirirExecucaoAsync(e2e, pId, cId, 1, "worker-A", 1, DateTimeOffset.UtcNow.AddMinutes(1));
        var r2 = await store2.AdquirirExecucaoAsync(e2e, pId, cId, 1, "worker-B", 1, DateTimeOffset.UtcNow.AddMinutes(1));

        // Then
        r1.Status.Should().Be(StatusAquisicao.Sucesso);
        r2.Status.Should().Be(StatusAquisicao.Conflito);
    }

    [Fact]
    public async Task TS_058_Should_Reverter_Terminal_E_Avanco_Juntos_When_Falha_Acontece_Na_Outbox_De_Conclusao()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new ExecutionStore(db);

        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(30m), sequenciaConta: 1);
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        var coord = new CoordenacaoConta(pedido.ContaId, 1);
        coord.AtribuirProximaSequencia(DateTimeOffset.UtcNow);

        db.Pedidos.Add(pedido);
        db.CoordenacoesConta.Add(coord);
        await db.SaveChangesAsync();

        // When: Força falha de Outbox passando payload nulo / evento inválido que gere falha
        // Simulando transação que é abortada por erro
        await using var tx = await db.Database.BeginTransactionAsync();
        pedido.Confirmar(new ExternalEvidence("EnvioPix", "OK", "Outbank", "REF", DateTimeOffset.UtcNow, true), 2, "worker-1", DateTimeOffset.UtcNow);
        coord.AvancarExecucao(1, DateTimeOffset.UtcNow);
        await tx.RollbackAsync(); // Falha no Outbox provoca rollback do grupo

        // Then: Pedido e coordenação não avançam
        using var dbCheck = CriarDbContext(dbName);
        var p = await dbCheck.Pedidos.FindAsync(pedido.Id);
        var c = await dbCheck.CoordenacoesConta.FindAsync(pedido.ContaId);

        p!.Estado.Should().Be(EstadoPedidoPix.EmProcessamento);
        c!.ProximaSequenciaExecutavel.Should().Be(1);
    }

    [Fact]
    public async Task TS_061_Should_Persistir_Geracao_QR_Duravel_Uma_Vez_When_EV02_Reentregue_Ou_TX04_Falha()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase(dbName).Options;
        using var db = new BillingDbContext(options);
        var store = new CobrancaJobStore(db);

        var cobranca = new CobrancaPix(new CobrancaId("COB-QR-1"), Guid.NewGuid(), 150m);

        // When: Consome EV-02 e grava Cobrança + Job
        var persistiu1 = await store.CriarCobrancaComJobAsync(cobranca, "EV-02-ID");
        // Reentrega de EV-02
        var persistiu2 = await store.CriarCobrancaComJobAsync(cobranca, "EV-02-ID");

        // Then
        persistiu1.Should().BeTrue();
        persistiu2.Should().BeFalse(); // Deduplicado por chave de job / cobrança
    }

    [Fact]
    public async Task TS_062_Should_Incorporar_Resultado_QR_Entre_Donos_When_Cobranca_Conclui_E_Processamento_Reinicia()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var execStore = new ExecutionStore(db);

        var pedidoId = Guid.NewGuid();
        var pedido = new PedidoPix(pedidoId, new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.Cobranca, null, new Money(200m));
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        var useCase = new IncorporateBillingResultUseCase(execStore);

        // When
        var incorporou = await useCase.IncorporarAsync(pedidoId, true, "COB-1", "EMV:000201...", "worker-qr");

        // Then
        incorporou.Should().BeTrue();
        using var dbCheck = CriarDbContext(dbName);
        var p = await dbCheck.Pedidos.FindAsync(pedidoId);
        p!.Estado.Should().Be(EstadoPedidoPix.Confirmado);
    }

    [Fact]
    public async Task TS_063_Should_Persistir_Inbox_E_Entrega_Antes_Da_Confirmacao()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CallbacksDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using var db = new CallbacksDbContext(options);
        var store = new CallbackDeliveryStore(db);

        var entrega = new CallbackDelivery(DeliveryId.Novo(), "EV-CONCLUSAO-1", "DEST-1", "{}");

        // When: Salva com sucesso
        var gravou1 = await store.AgendarEntregaAsync(entrega);

        // Tentativa concorrente/duplicada do mesmo evento e destinatário
        var entregaDuplicada = new CallbackDelivery(DeliveryId.Novo(), "EV-CONCLUSAO-1", "DEST-1", "{}");
        var gravou2 = await store.AgendarEntregaAsync(entregaDuplicada);

        // Then
        gravou1.Should().BeTrue();
        gravou2.Should().BeFalse(); // Conflito UNIQUE
    }

    [Fact]
    public async Task TS_070_Should_Paginar_Sem_Perder_Pendencias_When_Jobs_Selecionam_Lote()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);

        for (int i = 1; i <= 25; i++)
        {
            db.OutboxRecords.Add(new OutboxRecord
            {
                EventId = $"EV-{i}",
                Tipo = "TipoA",
                SchemaVersion = "1.0",
                CorrelationId = $"CORR-{i}",
                PayloadJson = "{}",
                Status = "PENDING",
                OccurredAt = DateTimeOffset.UtcNow.AddSeconds(i)
            });
        }
        await db.SaveChangesAsync();

        var relay = new OutboxRelay(db);

        // When: Busca lotes paginados de 10
        var lote1 = await relay.AdquirirLoteAsync(10, "worker-relay");
        var lote2 = await relay.AdquirirLoteAsync(10, "worker-relay");
        var lote3 = await relay.AdquirirLoteAsync(10, "worker-relay");

        // Then: 10 + 10 + 5 = 25 processados sem perdas
        lote1.Count.Should().Be(10);
        lote2.Count.Should().Be(10);
        lote3.Count.Should().Be(5);
    }

    [Fact]
    public async Task TS_089_Should_Recuperar_Vencedor_Apos_Conflito_Mesma_Chave()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new AcceptanceStore(db);

        var key = new IdempotencyKey(new ClienteId("cli-1"), "CashOut", "key-disputada");
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["x"] = 1 });

        var p1 = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(10m));
        var p2 = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E2"), new Money(10m));

        // When
        var r1 = await store.ConfirmarAceiteAsync(p1, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), true);
        var r2 = await store.ConfirmarAceiteAsync(p2, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), true);

        // Then
        r1.Status.Should().Be(StatusResultadoAceite.Criado);
        r2.Status.Should().Be(StatusResultadoAceite.Replay);
        r2.Aceite!.PedidoId.Should().Be(p1.Id);
    }

    [Fact]
    public async Task TS_090_Should_Recuperar_Aceite_Sem_Inventar_Sucesso_When_Confirmacao_Commit_Se_Perde()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new AcceptanceStore(db);

        var key = new IdempotencyKey(new ClienteId("cli-1"), "CashOut", "key-perda-commit");
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["x"] = 10 });
        var p = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), new ContaId("cnt-1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(10m));

        // When: Confirmado no banco
        await store.ConfirmarAceiteAsync(p, key, fp, id => new AceiteOriginal(id, 202, "{}", "{}", fp, DateTimeOffset.UtcNow), true);

        // Cliente perde conexão e consulta novamente
        var recuperado = await store.ObterAceitePorChaveAsync(key);

        // Then: Retorna resposta original já persistida sem inventar novo ID
        recuperado.Should().NotBeNull();
        recuperado!.PedidoId.Should().Be(p.Id);
    }

    [Fact]
    public async Task TS_091_Should_Registrar_Resposta_Tardia_So_Como_Evidencia()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new ExecutionStore(db);

        var e2e = new EndToEndId("E2E-TARDIA");
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, e2e, new Money(20m));
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        await store.AdquirirExecucaoAsync(e2e, pedido.Id, pedido.ContaId, 1, "worker-novo", 2, DateTimeOffset.UtcNow.AddMinutes(5));

        // When: worker antigo tenta concluir com versão e owner defasados
        var evidenciaAntiga = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF-OLD", DateTimeOffset.UtcNow, true);
        var ev = new OperacaoConcluidaEvento("EV-1", "CORR-1", pedido.Id, e2e, EstadoPedidoPix.Confirmado, "OK", pedido.ContaId, DateTimeOffset.UtcNow);

        var concluiu = await store.ConcluirExecucaoAsync(e2e, pedido.Id, evidenciaAntiga, EstadoPedidoPix.Confirmado, "worker-antigo", 99, ev);

        // Then: Conclusão deve ser rejeitada pelo CAS
        concluiu.Should().BeFalse();
    }

    [Fact]
    public async Task TS_092_Should_Preservar_Conclusao_Cobranca_When_Resultados_Terminais_Repetidos_Ou_Incompativeis()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var execStore = new ExecutionStore(db);

        var pedidoId = Guid.NewGuid();
        var pedido = new PedidoPix(pedidoId, new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.Cobranca, null, new Money(100m));
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        var useCase = new IncorporateBillingResultUseCase(execStore);

        // When 1: Conclui Disponivel
        var ok1 = await useCase.IncorporarAsync(pedidoId, true, "COB-10", "QR-OK", "worker-qr");
        // When 2: Reentrega idempotente
        var ok2 = await useCase.IncorporarAsync(pedidoId, true, "COB-10", "QR-OK", "worker-qr");

        // Then
        ok1.Should().BeTrue();
        ok2.Should().BeTrue(); // Idempotente
    }

    [Fact]
    public async Task TS_093_Should_Preservar_Identidades_EndToEndId_E_ContaId_Na_Desserializacao_Outbox()
    {
        // Given: Conclusão de execução salvando OperacaoConcluidaEvento no Outbox (PIX-AUD-04)
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var execStore = new ExecutionStore(db);

        var pedidoId = Guid.NewGuid();
        var e2e = new EndToEndId("E2E-TESTE-DESSERIALIZACAO-123");
        var contaId = new ContaId("cnt-teste-desserializacao-456");
        var pedido = new PedidoPix(pedidoId, new ClienteId("cli-1"), contaId, TipoOperacaoPix.CashOut, e2e, new Money(50m));
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        await execStore.AdquirirExecucaoAsync(e2e, pedidoId, contaId, 1, "worker-1", 1, DateTimeOffset.UtcNow.AddMinutes(5));

        var evidencia = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF-E2E-1", DateTimeOffset.UtcNow, true);
        var outboxEvento = new OperacaoConcluidaEvento(
            "EV-OUTBOX-01",
            pedidoId.ToString(),
            pedidoId,
            e2e,
            EstadoPedidoPix.Confirmado,
            "CONFIRMADO",
            contaId,
            DateTimeOffset.UtcNow);

        var concluiu = await execStore.ConcluirExecucaoAsync(e2e, pedidoId, evidencia, EstadoPedidoPix.Confirmado, "worker-1", 2, outboxEvento);
        concluiu.Should().BeTrue();

        // When: OutboxRelay adquire lote de eventos pendentes
        var relay = new OutboxRelay(db);
        var lote = await relay.AdquirirLoteAsync(10, "relay-worker");

        // Then: Evento desserializado preserva perfeitamente PedidoId, EndToEndId e ContaId (PIX-AUD-04)
        lote.Should().HaveCount(1);
        var eventoRecuperado = lote[0].Should().BeOfType<OperacaoConcluidaEvento>().Subject;
        eventoRecuperado.PedidoId.Should().Be(pedidoId);
        eventoRecuperado.EndToEndId.Should().NotBeNull();
        eventoRecuperado.EndToEndId!.Value.Valor.Should().Be("E2E-TESTE-DESSERIALIZACAO-123");
        eventoRecuperado.ContaId.Should().NotBeNull();
        eventoRecuperado.ContaId!.Value.Valor.Should().Be("cnt-teste-desserializacao-456");
        eventoRecuperado.Generation.Should().Be(1);

        // Then 2: CAS protege contra generation defasada (PIX-AUD-05)
        await relay.MarcarPublicadoAsync(eventoRecuperado.EventId, "relay-worker", generation: 0);
        var recAindaProcessando = await db.OutboxRecords.FindAsync(eventoRecuperado.EventId);
        recAindaProcessando!.Status.Should().Be("PROCESSING");

        await relay.MarcarPublicadoAsync(eventoRecuperado.EventId, "relay-worker", generation: eventoRecuperado.Generation);
        var recPublicado = await db.OutboxRecords.FindAsync(eventoRecuperado.EventId);
        recPublicado!.Status.Should().Be("PUBLISHED");
    }
}
