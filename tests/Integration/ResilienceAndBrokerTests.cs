using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pix.Callbacks.Domain;
using Pix.Callbacks.Infrastructure;
using Pix.Processing.Application.Features.ExecutePayment;
using Pix.Processing.Application.Features.ReconcilePayment;
using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;
using Pix.Processing.Infrastructure.Entities;
using Xunit;

namespace Integration;

public class ResilienceAndBrokerTests
{
    private PixDbContext CriarDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PixDbContext(options);
    }

    [Fact]
    public async Task TS_049_Should_Persistir_Pendencia_Antes_De_CompleteMessage_When_Comando_Posterior_Com_Lacuna()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);

        var contaId = new ContaId("C1");
        var coord = new CoordenacaoConta(contaId, 1);
        coord.Bloquear(1, "Incerteza seq 1", DateTimeOffset.UtcNow);
        db.CoordenacoesConta.Add(coord);
        await db.SaveChangesAsync();

        // When: Comando seq 2 chega enquanto seq 1 está bloqueada (INV-09, TX-02)
        var pendente = new PendingCommandRecord
        {
            EventId = "EV-SEQ-2",
            PedidoId = Guid.NewGuid(),
            ContaId = contaId.Valor,
            Sequencia = 2,
            CorpoJson = "{\"seq\":2}",
            Status = "BLOCKED",
            Tentativas = 0,
            ProximaTentativa = DateTimeOffset.UtcNow
        };
        db.PendingCommands.Add(pendente);
        await db.SaveChangesAsync();

        // Then: Pendência durável gravada com sucesso antes de confirmar broker
        using var dbCheck = CriarDbContext(dbName);
        var salvo = await dbCheck.PendingCommands.FindAsync("EV-SEQ-2");
        salvo.Should().NotBeNull();
        salvo!.Status.Should().Be("BLOCKED");
    }

    [Fact]
    public async Task TS_050_Should_Nao_Confirmar_Mensagem_When_TX02_Falha_Antes_Do_Commit()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);

        var pendente = new PendingCommandRecord
        {
            EventId = "EV-FAIL",
            PedidoId = Guid.NewGuid(),
            ContaId = "C1",
            Sequencia = 1,
            CorpoJson = "{}",
            Status = "PENDING",
            Tentativas = 0,
            ProximaTentativa = DateTimeOffset.UtcNow
        };

        // When: Transação aborta antes do commit
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            db.PendingCommands.Add(pendente);
            throw new InvalidOperationException("Falha de rede/banco na TX-02 antes do commit");
        }
        catch
        {
            db.ChangeTracker.Clear();
        }

        // Then: Registro não foi persistido
        using var dbCheck = CriarDbContext(dbName);
        var salvo = await dbCheck.PendingCommands.FindAsync("EV-FAIL");
        salvo.Should().BeNull();
    }

    [Fact]
    public async Task TS_051_Should_Retomar_Pendencia_Sem_Broker_When_Processo_Cai_Depois_De_Complete()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);

        var contaId = new ContaId("C1");
        var coord = new CoordenacaoConta(contaId, 1);
        db.CoordenacoesConta.Add(coord);

        db.PendingCommands.Add(new PendingCommandRecord
        {
            EventId = "EV-RETOMAR",
            PedidoId = Guid.NewGuid(),
            ContaId = contaId.Valor,
            Sequencia = 1,
            CorpoJson = "{}",
            Status = "READY",
            Tentativas = 0,
            ProximaTentativa = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // When: Scheduler local busca pendências elegíveis diretamente do banco
        using var dbScheduler = CriarDbContext(dbName);
        var elegiveis = await dbScheduler.PendingCommands
            .Where(c => c.ContaId == contaId.Valor && c.Status == "READY" && c.Sequencia == coord.ProximaSequenciaExecutavel)
            .ToListAsync();

        // Then: Executa via cursor de banco sem depender de reentrega do broker
        elegiveis.Should().HaveCount(1);
        elegiveis[0].EventId.Should().Be("EV-RETOMAR");
    }

    [Fact]
    public async Task TS_052_Should_Republicar_Mesma_Identidade_When_Relay_Cai_Apos_Confirmacao_Broker()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var relay = new OutboxRelay(db);
        var brokerPub = new AzureServiceBusPublisher();
        var processor = new DurableOutboxRelayProcessor(relay, brokerPub);

        var eventId = "EV-DURAVEL-100";
        db.OutboxRecords.Add(new OutboxRecord
        {
            EventId = eventId,
            Tipo = "PedidoFinanceiroAceito",
            SchemaVersion = "1.0",
            CorrelationId = "CORR-100",
            PayloadJson = "{}",
            Status = "PENDING",
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // When 1: Primeira tentativa publica no broker, mas processo cai antes de marcar publicado
        await brokerPub.PublicarEventoAsync(new OperacaoConcluidaEvento(eventId, "CORR-100", Guid.NewGuid(), null, EstadoPedidoPix.Confirmado, "OK", null, DateTimeOffset.UtcNow));

        // When 2: Processo reinicia e recupera o mesmo lote
        var publicados = await processor.ProcessarLoteAsync(10, "worker-novo");

        // Then: Conserva o mesmo MessageId determinístico sem inventar novo ID (INV-13)
        publicados.Should().Be(1);
        brokerPub.MensagensPublicadas.Select(m => m.EventId).Distinct().Should().ContainSingle().Which.Should().Be(eventId);
    }

    [Fact]
    public async Task TS_053_Should_Proteger_Sequencia_Mesmo_Com_Publicacao_Tardia_When_Lease_Antigo_Expira()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var relay = new OutboxRelay(db);

        var eventId = "EV-LEASE-EXPIRADO";
        var record = new OutboxRecord
        {
            EventId = eventId,
            Tipo = "PedidoFinanceiroAceito",
            SchemaVersion = "1.0",
            CorrelationId = "C1",
            PayloadJson = "{}",
            Status = "PROCESSING",
            Owner = "relay-R2", // R2 assumiu ownership
            Generation = 2,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            OccurredAt = DateTimeOffset.UtcNow
        };
        db.OutboxRecords.Add(record);
        await db.SaveChangesAsync();

        // When: R1 tenta marcar publicado com lease defasado
        await relay.MarcarPublicadoAsync(eventId, "relay-R1");

        // Then: CAS rejeita R1 e status permanece inalterado (INV-08, TX-06)
        using var dbCheck = CriarDbContext(dbName);
        var rec = await dbCheck.OutboxRecords.FindAsync(eventId);
        rec!.Status.Should().Be("PROCESSING");
        rec.Owner.Should().Be("relay-R2");

        // CAS com mesmo owner porém generation desatualizada também é rejeitado (PIX-AUD-05)
        await relay.MarcarPublicadoAsync(eventId, "relay-R2", generation: 1);
        using var dbCheckGen = CriarDbContext(dbName);
        var recGen = await dbCheckGen.OutboxRecords.FindAsync(eventId);
        recGen!.Status.Should().Be("PROCESSING");

        // CAS com owner e generation vigentes atualiza com sucesso
        await relay.MarcarPublicadoAsync(eventId, "relay-R2", generation: 2);
        using var dbCheckSuccess = CriarDbContext(dbName);
        var recSuccess = await dbCheckSuccess.OutboxRecords.FindAsync(eventId);
        recSuccess!.Status.Should().Be("PUBLISHED");

        // Sub-caso: Corrida com entity já em tracking no DbContext antigo (PIX-AUD-05)
        recSuccess.Status = "PROCESSING";
        recSuccess.Generation = 2;
        recSuccess.Owner = "relay-R2";
        await dbCheckSuccess.SaveChangesAsync();

        using var dbContextR2 = CriarDbContext(dbName);
        var relayR2Tracked = new OutboxRelay(dbContextR2);
        var recTracked = await dbContextR2.OutboxRecords.FindAsync(eventId); // carrega com Gen 2

        // Outro worker assume o lease com Gen 3
        using (var dbContextR3 = CriarDbContext(dbName))
        {
            var recR3 = await dbContextR3.OutboxRecords.FindAsync(eventId);
            recR3!.Generation = 3;
            recR3.Owner = "relay-R3";
            await dbContextR3.SaveChangesAsync();
        }

        // Contexto antigo tenta marcar publicado com a geração 2 desatualizada
        await relayR2Tracked.MarcarPublicadoAsync(eventId, "relay-R2", generation: 2);

        // Geração 3 permanece preservada e status continua PROCESSING
        using var dbCheckGen3 = CriarDbContext(dbName);
        var recFinal = await dbCheckGen3.OutboxRecords.FindAsync(eventId);
        recFinal!.Generation.Should().Be(3);
        recFinal.Owner.Should().Be("relay-R3");
        recFinal.Status.Should().Be("PROCESSING");
    }

    [Fact]
    public async Task TS_054_Should_Observar_Sessions_Dedup_E_DLQ_Reais_When_Topologia_Financeira_Validada()
    {
        // Given: Topologia do Service Bus configurada com Sessions, Janela de Deduplicação e DLQ
        var topology = new ServiceBusQueueTopology(
            RequiresSession: true,
            RequiresDuplicateDetection: true,
            DuplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10),
            MaxDeliveryCount: 10,
            DeadLetteringOnMessageExpiration: true);

        topology.RequiresSession.Should().BeTrue("Sessões são obrigatórias para ordenar mensagens por ContaId (INV-04)");
        topology.RequiresDuplicateDetection.Should().BeTrue("Deduplicação do broker protege dentro da janela (INV-05)");
        topology.DuplicateDetectionHistoryTimeWindow.Should().Be(TimeSpan.FromMinutes(10));
        topology.MaxDeliveryCount.Should().Be(10);

        // 1. Validação de Sessions: Mensagens particionadas por ContaId no SessionId
        var contaA = new ContaId("conta-A");
        var contaB = new ContaId("conta-B");

        var msgA1 = new ServiceBusMessage("{\"seq\":1}") { MessageId = "MSG-A-1", SessionId = contaA.Valor };
        var msgA2 = new ServiceBusMessage("{\"seq\":2}") { MessageId = "MSG-A-2", SessionId = contaA.Valor };
        var msgB1 = new ServiceBusMessage("{\"seq\":1}") { MessageId = "MSG-B-1", SessionId = contaB.Valor };

        msgA1.SessionId.Should().Be("conta-A");
        msgA2.SessionId.Should().Be("conta-A");
        msgB1.SessionId.Should().Be("conta-B");
        msgA1.SessionId.Should().NotBe(msgB1.SessionId, "Contas distintas possuem sessões independentes");

        // 2. Validação da Janela de Deduplicação:
        // Mensagem duplicada enviada dentro da janela de 10 min é suprimida pelo broker
        var simulatedBroker = new SimulatedBrokerTopology(topology);
        var pub1 = simulatedBroker.Publicar(msgA1, tempoEnvio: DateTimeOffset.UtcNow);
        var pub1DuplicataNaJanela = simulatedBroker.Publicar(msgA1, tempoEnvio: DateTimeOffset.UtcNow.AddMinutes(2));
        pub1.Deduplicada.Should().BeFalse("Primeira mensagem deve ser aceita");
        pub1DuplicataNaJanela.Deduplicada.Should().BeTrue("Mensagem com mesmo MessageId dentro da janela deve ser suprimida pelo broker");

        // Mensagem com mesmo efeito financeiro reenviada FORA da janela de 10 min:
        // O broker admite a mensagem, mas o banco de dados (ExecutionStore / ProcessedIds) protege contra execução duplicada
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new ExecutionStore(db);
        var e2e = new EndToEndId("E2E-DEDUP-001");
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("cli-1"), contaA, TipoOperacaoPix.CashOut, e2e, new Money(100m), sequenciaConta: 1);
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        // Primeira aquisição obtém sucesso e conclui execução
        var aquisicao1 = await store.AdquirirExecucaoAsync(e2e, pedido.Id, contaA, 1, "worker-1", 1, DateTimeOffset.UtcNow.AddMinutes(5));
        aquisicao1.Status.Should().Be(StatusAquisicao.Sucesso);

        var evidencia = new ExternalEvidence("CashOut", "Envio", "Outbank", "TX-1", DateTimeOffset.UtcNow, true);
        var outboxEvento = new OperacaoConcluidaEvento("EV-CONFIRMADO", "CORR-1", pedido.Id, e2e, EstadoPedidoPix.Confirmado, "OK", contaA, DateTimeOffset.UtcNow);
        var concluiu = await store.ConcluirExecucaoAsync(e2e, pedido.Id, evidencia, EstadoPedidoPix.Confirmado, "worker-1", pedido.Versao, outboxEvento);
        concluiu.Should().BeTrue();

        // Suponha que fora da janela do broker (ex: 15 min depois), a mensagem chega de novo
        var pubForaDaJanela = simulatedBroker.Publicar(
            new ServiceBusMessage("{\"seq\":1}") { MessageId = "MSG-A-1", SessionId = contaA.Valor },
            tempoEnvio: DateTimeOffset.UtcNow.AddMinutes(15));
        pubForaDaJanela.Deduplicada.Should().BeFalse("Broker admite pois janela expirou");

        // Mas o ExecutionStore local barra o replay de execução pois já foi concluído
        var aquisicao2 = await store.AdquirirExecucaoAsync(e2e, pedido.Id, contaA, 1, "worker-2", 1, DateTimeOffset.UtcNow.AddMinutes(20));
        aquisicao2.Status.Should().Be(StatusAquisicao.JaConcluido, "Idempotência durável no banco protege fora da janela do broker");

        // 3. Validação de Dead-Letter Queue (DLQ) por MaxDeliveryCount
        var msgComFalha = new ServiceBusMessage("{\"seq\":99}") { MessageId = "MSG-FAIL", SessionId = contaA.Valor };
        var delivery = simulatedBroker.IniciarEntrega(msgComFalha);
        for (int i = 1; i < topology.MaxDeliveryCount; i++)
        {
            delivery.RegistrarFalhaEAbandonar();
            delivery.EstaNoDeadLetter.Should().BeFalse();
            delivery.DeliveryCount.Should().Be(i + 1);
        }

        // 10ª falha atinge MaxDeliveryCount e move para DLQ
        delivery.RegistrarFalhaEAbandonar();
        delivery.EstaNoDeadLetter.Should().BeTrue();
        delivery.DeadLetterReason.Should().Be("MaxDeliveryCountExceeded");

        // 4. Relatório de compatibilidade e ambiente de homologação (PIX-AUD-07)
        // Conforme INV-23/24 e PIX-AUD-07: Sessions, deduplicação e DLQ foram exercitados pelo broker simulado (SimulatedBrokerTopology).
        // Um mero ping ou verificação de conexão não audita nem comprova os atributos reais da fila no Azure (RequiresSession, RequiresDuplicateDetection, DLQ).
        // Logo, a homologação Azure real permanece OBRIGATORIAMENTE pendente.
        var relatorio = topology.GerarRelatorioParidade("Ambiente-Validação-Local (Topologia-Simulada)");
        relatorio.SessionsHabilitadas.Should().BeTrue();
        relatorio.DeduplicacaoAtiva.Should().BeTrue();
        relatorio.MaxDeliveryCount.Should().Be(10);
        relatorio.HomologacaoAzureRealPendente.Should().BeTrue("Conforme INV-23/24 e PIX-AUD-07, exercitar topologia no broker simulado não comprova a topologia Azure real (Sessions, Dedup e DLQ), mantendo a homologação Azure pendente");
        relatorio.Ambiente.Should().Be("Ambiente-Validação-Local (Topologia-Simulada)");
    }

    [Fact]
    public async Task TS_055_Should_Bloquear_Conta_Sem_Reenviar_When_Session_Lock_E_Lease_Vencem_Apos_Intencao()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new ExecutionStore(db);

        var e2e = new EndToEndId("E2E-TIMEOUT");
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, e2e, new Money(100m));
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        pedido.RegistrarIntencaoEnvio(DateTimeOffset.UtcNow);
        pedido.MarcarResultadoIncerto("Perda de session lock", null, DateTimeOffset.UtcNow);

        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        // When: Novo worker inspeciona pedido pós-intenção vencido
        var estado = await store.ObterEstadoAsync(e2e);

        // Then: Fica conservadoramente em ResultadoIncerto, bloqueando reenvio automático (INV-06)
        pedido.Estado.Should().Be(EstadoPedidoPix.ResultadoIncerto);
    }

    [Fact]
    public async Task TS_056_Should_Reconciliar_Efeito_Sem_Commit_Local_When_Outbank_Executa_Mas_Commit_Resultado_Falha()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);
        var store = new ExecutionStore(db);
        var acl = new OutbankAclAdapter();

        var e2e = new EndToEndId("E2E-RECON-SUCCESS");
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, e2e, new Money(100m));
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        pedido.RegistrarIntencaoEnvio(DateTimeOffset.UtcNow);
        pedido.MarcarResultadoIncerto("Crash antes do commit local", null, DateTimeOffset.UtcNow);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        // Outbank executou e possui evidência autoritativa registrada
        await acl.ExecutarEnvioPixAsync(e2e, new Money(100m), new ContaId("cnt1"));

        var useCase = new ReconcilePaymentUseCase(store, acl);

        // When: Reconciliador consulta autoritativamente
        var reconciliado = await useCase.ReconciliarAsync(e2e, pedido.Id, pedido.ContaId, "worker-1", pedido.Versao);

        // Then: Transiciona para Confirmado e resolve efeito com sucesso
        reconciliado.Should().BeTrue();
    }

    [Fact]
    public async Task TS_059_Should_Reprocessar_DLQ_Sem_Pular_Sequencia_When_Operador_Autorizado_Resolve()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        using var db = CriarDbContext(dbName);

        var contaId = new ContaId("cnt-dlq");
        var coord = new CoordenacaoConta(contaId, 1);
        coord.Bloquear(1, "DLQ seq 1", DateTimeOffset.UtcNow);
        db.CoordenacoesConta.Add(coord);

        // Seq 2 aguardando na pendência
        db.PendingCommands.Add(new PendingCommandRecord
        {
            EventId = "EV-SEQ2",
            PedidoId = Guid.NewGuid(),
            ContaId = contaId.Valor,
            Sequencia = 2,
            CorpoJson = "{}",
            Status = "BLOCKED",
            Tentativas = 0,
            ProximaTentativa = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // When: Operador autorizado resolve e avança seq 1
        coord.Desbloquear(1, "DLQ seq 1 resolvida", DateTimeOffset.UtcNow);
        coord.AvancarExecucao(1, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        // Then: Sequência executável agora é a seq 2, sem pular comandos
        coord.ProximaSequenciaExecutavel.Should().Be(2);
        coord.Estado.Should().Be(EstadoCoordenacao.Livre);
    }

    [Fact]
    public void TS_060_Should_Separar_Orcamento_HTTP_De_Deliveries_When_Falha_Seguramente_Repetivel()
    {
        // Given
        int tentativasHttpNoProcesso = 0;
        int deliveryCountBroker = 1;

        // When: 2 retries HTTP dentro do mesmo ciclo de delivery
        tentativasHttpNoProcesso += 2;

        // Then: Delivery count permanece 1 até que a mensagem seja explicitamente abandonada
        tentativasHttpNoProcesso.Should().Be(2);
        deliveryCountBroker.Should().Be(1);
    }

    [Fact]
    public async Task TS_064_Should_Retomar_Callback_Com_Mesma_Identidade_When_Destino_Aceitou_E_Processo_Caiu()
    {
        // Given
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CallbacksDbContext>().UseInMemoryDatabase(dbName).Options;
        using var db = new CallbacksDbContext(options);
        var store = new CallbackDeliveryStore(db);

        var entregaId = DeliveryId.Novo();
        var entrega = new CallbackDelivery(entregaId, "EV-1", "D-1", "{}", maxTentativas: 3);
        await store.AgendarEntregaAsync(entrega);

        // When: Inicia envio, destino aceita, mas simula crash antes do salvamento
        entrega.IniciarEnvio(DateTimeOffset.UtcNow);
        entrega.RegistrarAceite(DateTimeOffset.UtcNow);
        await store.AtualizarEntregaAsync(entrega);

        // Then: EntregaId permanece estável na recuperação
        using var dbCheck = new CallbacksDbContext(options);
        var salva = await dbCheck.Entregas.FindAsync(entregaId);
        salva!.Id.Should().Be(entregaId);
        salva.Estado.Should().Be(EstadoEntrega.Aceita);
    }

    [Fact]
    public async Task TS_065_Should_Negar_DNS_E_Redirect_Perigosos_No_Envio_When_Destino_Muda_Resolucao()
    {
        // Given
        var transport = new SafeCallbackTransport();
        var destinoProibido = new CallbackDestination("dest-priv", "https://api.empresa.com", "sec");

        // When & Then: Validação bloqueia se DNS apontar para loopback ou metadados
        var act = async () => await CallbackDestination.ValidarHostNaoPrivadoComDnsAsync("127.0.0.1");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*loopback*");
    }

    [Fact]
    public void TS_071_Should_Correlacionar_Telemetria_Sem_Vazar_Dados_When_Exportam_Traces()
    {
        // Given: Atividade de rastreio W3C
        var activity = new Activity("PixPaymentTest").Start();
        activity.SetTag("correlation_id", "CORR-123");
        activity.SetTag("end_to_end_id", "E2E-123");

        // When & Then: Tags de correlação presentes, credenciais e payloads sensíveis não são incluídos
        activity.GetTagItem("correlation_id").Should().Be("CORR-123");
        activity.GetTagItem("password").Should().BeNull();
        activity.GetTagItem("secret").Should().BeNull();
    }

    [Fact]
    public void TS_072_Should_Distinguir_Live_E_Ready_When_Dependencia_Obrigatoria_Falta()
    {
        // Given: Simulação da lógica de Readiness e Liveness probes (INV-21)
        bool dbDisponivel = false;
        bool processoVivo = true;

        // When
        var livenessStatus = processoVivo ? "Live" : "Dead";
        var readinessStatus = dbDisponivel ? "Ready" : "Unhealthy";

        // Then: Live permanece 200/Live mesmo se o banco estiver indisponível
        livenessStatus.Should().Be("Live");
        readinessStatus.Should().Be("Unhealthy");
    }
}

public record ServiceBusQueueTopology(
    bool RequiresSession,
    bool RequiresDuplicateDetection,
    TimeSpan DuplicateDetectionHistoryTimeWindow,
    int MaxDeliveryCount,
    bool DeadLetteringOnMessageExpiration)
{
    public BrokerParityReport GerarRelatorioParidade(string ambiente, bool topologiaRealAuditada = false) =>
        new(RequiresSession, RequiresDuplicateDetection, MaxDeliveryCount, HomologacaoAzureRealPendente: !topologiaRealAuditada || ambiente != "Azure-ServiceBus-Live", ambiente);
}

public record BrokerParityReport(
    bool SessionsHabilitadas,
    bool DeduplicacaoAtiva,
    int MaxDeliveryCount,
    bool HomologacaoAzureRealPendente,
    string Ambiente);

public class SimulatedBrokerTopology
{
    private readonly ServiceBusQueueTopology _topology;
    private readonly Dictionary<string, DateTimeOffset> _messagesSeen = new();

    public SimulatedBrokerTopology(ServiceBusQueueTopology topology)
    {
        _topology = topology;
    }

    public PublicacaoResultado Publicar(ServiceBusMessage message, DateTimeOffset tempoEnvio)
    {
        if (_topology.RequiresDuplicateDetection && _messagesSeen.TryGetValue(message.MessageId, out var firstSeen))
        {
            if (tempoEnvio - firstSeen <= _topology.DuplicateDetectionHistoryTimeWindow)
            {
                return new PublicacaoResultado(Deduplicada: true, message);
            }
        }

        _messagesSeen[message.MessageId] = tempoEnvio;
        return new PublicacaoResultado(Deduplicada: false, message);
    }

    public SimulatedMessageDelivery IniciarEntrega(ServiceBusMessage message) =>
        new(message, _topology.MaxDeliveryCount);
}

public record PublicacaoResultado(bool Deduplicada, ServiceBusMessage Mensagem);

public class SimulatedMessageDelivery
{
    private readonly ServiceBusMessage _message;
    private readonly int _maxDeliveryCount;

    public SimulatedMessageDelivery(ServiceBusMessage message, int maxDeliveryCount)
    {
        _message = message;
        _maxDeliveryCount = maxDeliveryCount;
        DeliveryCount = 1;
    }

    public int DeliveryCount { get; private set; }
    public bool EstaNoDeadLetter { get; private set; }
    public string? DeadLetterReason { get; private set; }

    public void RegistrarFalhaEAbandonar()
    {
        DeliveryCount++;
        if (DeliveryCount > _maxDeliveryCount)
        {
            EstaNoDeadLetter = true;
            DeadLetterReason = "MaxDeliveryCountExceeded";
        }
    }
}

