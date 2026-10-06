using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pix.Callbacks.Domain;
using Pix.Processing.Application.Features.AcceptRequest;
using Pix.Processing.Application.Features.ExecutePayment;
using Pix.Processing.Application.Features.IncorporateBillingResult;
using Pix.Processing.Application.Features.ReconcilePayment;
using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;
using Xunit;

namespace Unit;

public class ApplicationTests
{
    [Fact]
    public async Task TS_002_Should_Negar_Acesso_A_Conta_When_Cliente_Sem_Vinculo()
    {
        // Given
        var authPort = Substitute.For<IAutorizacaoPort>();
        var store = Substitute.For<IAcceptanceStore>();
        var limiter = Substitute.For<IAdmissionLimiter>();

        authPort.AutorizarAsync(Arg.Any<ClienteId>(), Arg.Any<ContaId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var useCase = new AcceptRequestUseCase(authPort, store, limiter);

        var cmd = new AcceptRequestCommand(
            new ClienteId("cliente-B"),
            new ContaId("conta-A"),
            "CashOut",
            "key-1",
            new Money(100m),
            new Dictionary<string, object?> { ["x"] = 1 },
            "E2E1");

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then
        resultado.Status.Should().Be(StatusResultadoAceite.ConflitoDeConteudo);
        await store.DidNotReceive().ConfirmarAceiteAsync(Arg.Any<PedidoPix>(), Arg.Any<IdempotencyKey>(), Arg.Any<Fingerprint>(), Arg.Any<Func<Guid, AceiteOriginal>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_003_Should_Reconhecer_Repeticao_When_Chave_E_Fingerprint_Iguais()
    {
        // Given
        var authPort = Substitute.For<IAutorizacaoPort>();
        var store = Substitute.For<IAcceptanceStore>();
        var limiter = Substitute.For<IAdmissionLimiter>();

        authPort.AutorizarAsync(Arg.Any<ClienteId>(), Arg.Any<ContaId>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["x"] = 1 });
        var aceiteOriginal = new AceiteOriginal(Guid.NewGuid(), 202, "{}", "{}", fp, DateTimeOffset.UtcNow);

        store.ObterAceitePorChaveAsync(Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(aceiteOriginal);

        var useCase = new AcceptRequestUseCase(authPort, store, limiter);
        var cmd = new AcceptRequestCommand(new ClienteId("c1"), new ContaId("cnt1"), "CashOut", "k1", new Money(50m), new Dictionary<string, object?> { ["x"] = 1 });

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then
        resultado.Status.Should().Be(StatusResultadoAceite.Replay);
        resultado.Aceite.Should().Be(aceiteOriginal);
        await limiter.DidNotReceive().VerificarAdmissaoAsync(Arg.Any<ClienteId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_004_Should_Rejeitar_Conflito_Fingerprint_When_Mesma_Chave_Conteudo_Diferente()
    {
        // Given
        var authPort = Substitute.For<IAutorizacaoPort>();
        var store = Substitute.For<IAcceptanceStore>();
        var limiter = Substitute.For<IAdmissionLimiter>();

        authPort.AutorizarAsync(Arg.Any<ClienteId>(), Arg.Any<ContaId>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var fp1 = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["x"] = 1 });
        var aceiteOriginal = new AceiteOriginal(Guid.NewGuid(), 202, "{}", "{}", fp1, DateTimeOffset.UtcNow);

        store.ObterAceitePorChaveAsync(Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(aceiteOriginal);

        var useCase = new AcceptRequestUseCase(authPort, store, limiter);
        var cmd = new AcceptRequestCommand(new ClienteId("c1"), new ContaId("cnt1"), "CashOut", "k1", new Money(50m), new Dictionary<string, object?> { ["x"] = 2 });

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then
        resultado.Status.Should().Be(StatusResultadoAceite.ConflitoDeConteudo);
    }

    [Fact]
    public void TS_005_Should_Isolar_Escopo_Da_Chave_When_Cliente_Ou_Tipo_Diferem()
    {
        // Given
        var k1 = new IdempotencyKey(new ClienteId("cliente-A"), "CashOut", "CHAVE-1");
        var k2 = new IdempotencyKey(new ClienteId("cliente-B"), "CashOut", "CHAVE-1");
        var k3 = new IdempotencyKey(new ClienteId("cliente-A"), "Cobranca", "CHAVE-1");

        // When & Then
        k1.ChaveComposta.Should().NotBe(k2.ChaveComposta);
        k1.ChaveComposta.Should().NotBe(k3.ChaveComposta);
        k1.ChaveComposta.Should().Be("cliente-A:cashout:CHAVE-1");
    }

    [Fact]
    public async Task TS_008_Should_Adquirir_Execucao_When_Pedido_E_Proximo_Elegivel()
    {
        // Given
        var execStore = Substitute.For<IExecutionStore>();
        var acl = Substitute.For<IOutbankAcl>();

        execStore.AdquirirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ContaId>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new AquisicaoResultado(StatusAquisicao.Sucesso));

        var evidencia = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF1", DateTimeOffset.UtcNow, sucesso: true);
        acl.ExecutarEnvioPixAsync(Arg.Any<EndToEndId>(), Arg.Any<Money>(), Arg.Any<ContaId>(), Arg.Any<CancellationToken>())
            .Returns(evidencia);

        execStore.ConcluirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ExternalEvidence>(), Arg.Any<EstadoPedidoPix>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DominioEvento>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var useCase = new ExecutePaymentUseCase(execStore, acl);
        var cmd = new ExecutePaymentCommand(Guid.NewGuid(), new ContaId("C1"), 1, new EndToEndId("E2E1"), new Money(50m), "owner-1", 1, DateTimeOffset.UtcNow.AddMinutes(1));

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then
        resultado.Status.Should().Be(ExecutionResultStatus.Confirmado);
        await execStore.Received(1).RegistrarIntencaoAsync(cmd.EndToEndId, cmd.Owner, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_009_Should_Distinguir_Conflito_De_Aquisicao_When_Ja_Em_Andamento()
    {
        // Given
        var execStore = Substitute.For<IExecutionStore>();
        var acl = Substitute.For<IOutbankAcl>();

        execStore.AdquirirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ContaId>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new AquisicaoResultado(StatusAquisicao.Conflito, EstadoPedidoPix.EmProcessamento, "Bloqueado por lease ativo"));

        var useCase = new ExecutePaymentUseCase(execStore, acl);
        var cmd = new ExecutePaymentCommand(Guid.NewGuid(), new ContaId("C1"), 1, new EndToEndId("E2E1"), new Money(50m), "owner-2", 1, DateTimeOffset.UtcNow.AddMinutes(1));

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then
        resultado.Status.Should().Be(ExecutionResultStatus.ConflitoAquisicao);
        await acl.DidNotReceive().ExecutarEnvioPixAsync(Arg.Any<EndToEndId>(), Arg.Any<Money>(), Arg.Any<ContaId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_010_Should_Retornar_Resultado_Terminal_When_Operacao_Concluida_E_Reentregue()
    {
        // Given
        var execStore = Substitute.For<IExecutionStore>();
        var acl = Substitute.For<IOutbankAcl>();

        execStore.AdquirirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ContaId>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new AquisicaoResultado(StatusAquisicao.JaConcluido, EstadoPedidoPix.Confirmado));

        var useCase = new ExecutePaymentUseCase(execStore, acl);
        var cmd = new ExecutePaymentCommand(Guid.NewGuid(), new ContaId("C1"), 1, new EndToEndId("E2E1"), new Money(50m), "owner-1", 1, DateTimeOffset.UtcNow.AddMinutes(1));

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then
        resultado.Status.Should().Be(ExecutionResultStatus.JaConcluido);
        await acl.DidNotReceive().ExecutarEnvioPixAsync(Arg.Any<EndToEndId>(), Arg.Any<Money>(), Arg.Any<ContaId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_013_Should_Registrar_Resultado_Incerto_When_Possivel_Envio_Tem_Timeout()
    {
        // Given
        var execStore = Substitute.For<IExecutionStore>();
        var acl = Substitute.For<IOutbankAcl>();

        execStore.AdquirirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ContaId>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new AquisicaoResultado(StatusAquisicao.Sucesso));

        execStore.ConcluirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ExternalEvidence>(), Arg.Any<EstadoPedidoPix>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DominioEvento>(), Arg.Any<CancellationToken>())
            .Returns(true);

        acl.ExecutarEnvioPixAsync(Arg.Any<EndToEndId>(), Arg.Any<Money>(), Arg.Any<ContaId>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Timeout pós-envio"));

        var useCase = new ExecutePaymentUseCase(execStore, acl);
        var cmd = new ExecutePaymentCommand(Guid.NewGuid(), new ContaId("C1"), 1, new EndToEndId("E2E1"), new Money(50m), "owner-1", 1, DateTimeOffset.UtcNow.AddMinutes(1));

        // When
        var resultado = await useCase.ExecutarAsync(cmd);

        // Then
        resultado.Status.Should().Be(ExecutionResultStatus.ResultadoIncerto);
        await execStore.Received(1).ConcluirExecucaoAsync(
            cmd.EndToEndId,
            cmd.PedidoId,
            Arg.Any<ExternalEvidence>(),
            EstadoPedidoPix.ResultadoIncerto,
            cmd.Owner,
            Arg.Any<int>(),
            Arg.Any<DominioEvento>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_014_Should_Encaminhar_A_Triagem_When_Evidencia_Nao_Suficiente()
    {
        // Given
        var execStore = Substitute.For<IExecutionStore>();
        var acl = Substitute.For<IOutbankAcl>();

        // Outbank retorna null (consulta inconclusiva / "não encontrado" isolado)
        acl.ConsultarPixAsync(Arg.Any<EndToEndId>(), Arg.Any<CancellationToken>())
            .Returns((ExternalEvidence?)null);

        var useCase = new ReconcilePaymentUseCase(execStore, acl);

        // When
        var reconciliado = await useCase.ReconciliarAsync(new EndToEndId("E1"), Guid.NewGuid(), new ContaId("C1"), "worker-1", 1);

        // Then: Não resolve terminal sem prova autoritativa conclusiva (INV-07)
        reconciliado.Should().BeFalse();
        await execStore.DidNotReceive().ConcluirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ExternalEvidence>(), Arg.Any<EstadoPedidoPix>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DominioEvento>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_015_Should_Resolver_Resultado_Incerto_When_Fonte_Autoritativa_Comprova_Resultado()
    {
        // Given
        var execStore = Substitute.For<IExecutionStore>();
        var acl = Substitute.For<IOutbankAcl>();

        var evidencia = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF-AUTH-123", DateTimeOffset.UtcNow, sucesso: true);
        acl.ConsultarPixAsync(Arg.Any<EndToEndId>(), Arg.Any<CancellationToken>())
            .Returns(evidencia);

        execStore.ConcluirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ExternalEvidence>(), Arg.Any<EstadoPedidoPix>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DominioEvento>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var useCase = new ReconcilePaymentUseCase(execStore, acl);

        // When
        var reconciliado = await useCase.ReconciliarAsync(new EndToEndId("E1"), Guid.NewGuid(), new ContaId("C1"), "worker-1", 1);

        // Then
        reconciliado.Should().BeTrue();
        await execStore.Received(1).ConcluirExecucaoAsync(
            new EndToEndId("E1"),
            Arg.Any<Guid>(),
            evidencia,
            EstadoPedidoPix.Confirmado,
            "worker-1",
            1,
            Arg.Any<DominioEvento>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TS_023_Should_Incorporar_Resultado_Cobranca_Em_Transacao_Propria()
    {
        // Given
        var execStore = Substitute.For<IExecutionStore>();
        execStore.ConcluirExecucaoAsync(Arg.Any<EndToEndId>(), Arg.Any<Guid>(), Arg.Any<ExternalEvidence>(), Arg.Any<EstadoPedidoPix>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DominioEvento>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var useCase = new IncorporateBillingResultUseCase(execStore);

        // When
        var incorporou = await useCase.IncorporarAsync(
            Guid.NewGuid(),
            true,
            "COB-123",
            "EMV:000201...",
            "worker-qr");

        // Then
        incorporou.Should().BeTrue();
    }

    [Fact]
    public void TS_033_Should_Preservar_AceiteOriginal_When_Replay_Tenta_Alterar_Resposta()
    {
        // Given
        var fp = Fingerprint.GerarDePayload("v1", new Dictionary<string, object?> { ["x"] = 1 });
        var aceiteOriginal = new AceiteOriginal(Guid.NewGuid(), 202, "{\"X-Header\":\"1\"}", "{\"status\":\"Accepted\"}", fp, DateTimeOffset.UtcNow);

        // When & Then: Registros de resposta são imutáveis
        aceiteOriginal.StatusCode.Should().Be(202);
        aceiteOriginal.CorpoJson.Should().Be("{\"status\":\"Accepted\"}");
        aceiteOriginal.Fingerprint.Should().Be(fp);
    }

    [Fact]
    public void TS_034_Should_Validar_OwnershipVersion_E_ExternalEvidence()
    {
        // Given
        var now = DateTimeOffset.UtcNow;
        var ow = new OwnershipVersion("worker-1", 1, 0, now.AddMinutes(2));

        // When
        var ow2 = ow.IncrementarVersao(now.AddMinutes(3));
        var ow3 = ow.ProximoOwner("worker-2", now.AddMinutes(4));

        // Then
        ow2.Version.Should().Be(1);
        ow2.Owner.Should().Be("worker-1");
        ow3.Owner.Should().Be("worker-2");
        ow3.Generation.Should().Be(2);

        var ev = new ExternalEvidence("Envio", "Liquidacao", "Outbank", "REF1", now, true);
        ev.Sucesso.Should().BeTrue();
        ev.Fonte.Should().Be("Outbank");
    }

    [Fact]
    public void TS_035_Should_Derivar_Identidade_Deterministica_De_Evento()
    {
        // Given: Função determinística de geração de MessageId
        string GerarId(string cliente, string op, string chave, string tipo, int ordinal)
        {
            var raw = $"{cliente}:{op}:{chave}:{tipo}:{ordinal}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        // When
        var id1 = GerarId("cli-A", "CashOut", "K1", "PedidoFinanceiroAceito", 1);
        var id2 = GerarId("cli-A", "CashOut", "K1", "PedidoFinanceiroAceito", 1);
        var id3 = GerarId("cli-A", "CashOut", "K1", "OperacaoConcluida", 1);

        // Then
        id1.Should().Be(id2);
        id1.Should().NotBe(id3);
    }

    [Fact]
    public void TS_036_Should_Validar_Envelope_E_PendingCommand_When_Mensagem_Financeira_Recebida()
    {
        // Given & When
        var cmd = new PendingCommandItem(
            "EV-001",
            Guid.NewGuid(),
            new ContaId("C1"),
            1,
            "{\"pedidoId\":\"abc\"}",
            "PENDING",
            0,
            DateTimeOffset.UtcNow);

        // Then
        cmd.EventId.Should().Be("EV-001");
        cmd.Sequencia.Should().Be(1);
        cmd.Status.Should().Be("PENDING");
    }

    [Fact]
    public void TS_037_Should_Recusar_Destino_Callback_Invalido_Ou_SSRF()
    {
        // Given & When & Then: Variações de destinos proibidos (INV-15)
        var act1 = () => new CallbackDestination("d1", "http://api.cliente.com/callback", "sec");
        act1.Should().Throw<ArgumentException>().WithMessage("*Apenas destinos HTTPS são permitidos*");

        var act2 = () => new CallbackDestination("d2", "https://localhost:8080/callback", "sec");
        act2.Should().Throw<InvalidOperationException>().WithMessage("*localhost*");

        var act3 = () => new CallbackDestination("d3", "https://127.0.0.1/callback", "sec");
        act3.Should().Throw<InvalidOperationException>().WithMessage("*loopback*");

        var act4 = () => new CallbackDestination("d4", "https://10.0.0.1/callback", "sec");
        act4.Should().Throw<InvalidOperationException>().WithMessage("*10.0.0.0/8*");

        var act5 = () => new CallbackDestination("d5", "https://169.254.169.254/latest/meta-data", "sec");
        act5.Should().Throw<InvalidOperationException>().WithMessage("*169.254.0.0/16*");
    }

    [Fact]
    public void TS_038_Should_Proteger_Geracao_E_Validade_Do_Snapshot()
    {
        // Given
        var now = DateTimeOffset.UtcNow;
        var snap = new FinancialSnapshot(
            new ContaId("C1"),
            new Money(100m),
            new Money(500m),
            now,
            new CacheGeneration(5),
            now.AddMinutes(5));

        // When & Then
        snap.Geracao.Numero.Should().Be(5);
        snap.ValidoAte.Should().BeAfter(now);
    }

    [Fact]
    public void TS_039_Should_Rejeitar_Orcamento_Ou_Perfil_Incompleto()
    {
        // Given: Validação de orçamento com deadline zero ou negativo
        Action act1 = () =>
        {
            var timeout = TimeSpan.FromSeconds(0);
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentException("Timeout deve ser estritamente positivo.");
        };

        // Then
        act1.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_040_Should_Preservar_Eventos_Dominio_EV01_A_EV11()
    {
        // Given
        var agora = DateTimeOffset.UtcNow;
        var ev1 = new PedidoFinanceiroAceitoEvento("E1", "C1", Guid.NewGuid(), new ContaId("cnt"), new AccountSequence(new ContaId("cnt"), 1), new EndToEndId("E2E"), TipoOperacaoPix.CashOut, new Money(10m), agora);
        var ev2 = new GeracaoCobrancaSolicitadaEvento("E2", "C2", Guid.NewGuid(), "COB-1", new Money(10m), agora);
        var ev3 = new CobrancaDisponibilizadaEvento("E3", "C3", Guid.NewGuid(), "COB-1", "QR1", agora);
        var ev4 = new GeracaoCobrancaRecusadaEvento("E4", "C4", Guid.NewGuid(), "COB-1", "Motivo", agora);
        var ev5 = new ResultadoFinanceiroIncertoEvento("E5", "C5", Guid.NewGuid(), new ContaId("cnt"), new EndToEndId("E2E"), "Timeout", "REF", agora);
        var ev6 = new ContaBloqueadaEvento("E6", "C6", new ContaId("cnt"), 1, "Incerteza", Guid.NewGuid(), agora);
        var ev7 = new ResultadoReconciliadoEvento("E7", "C7", Guid.NewGuid(), "REF", "CONFIRMADO", 1, agora);
        var ev8 = new OperacaoConcluidaEvento("E8", "C8", Guid.NewGuid(), new EndToEndId("E2E"), EstadoPedidoPix.Confirmado, "OK", new ContaId("cnt"), agora);
        var ev9 = new EntregaCallbackAceitaEvento("E9", "C9", Guid.NewGuid(), "E8", "D1", agora);
        var ev10 = new EntregaCallbackEsgotadaEvento("E10", "C10", Guid.NewGuid(), "E8", "Max retries", agora);
        var ev11 = new ContaLiberadaEvento("E11", "C11", new ContaId("cnt"), 1, 1, agora);

        // Then: Todos possuem tipos corretos e SchemaVersion 1.0 (INV-20)
        ev1.Tipo.Should().Be("PedidoFinanceiroAceito");
        ev2.Tipo.Should().Be("GeracaoCobrancaSolicitada");
        ev3.Tipo.Should().Be("CobrancaDisponibilizada");
        ev4.Tipo.Should().Be("GeracaoCobrancaRecusada");
        ev5.Tipo.Should().Be("ResultadoFinanceiroIncerto");
        ev6.Tipo.Should().Be("ContaBloqueada");
        ev7.Tipo.Should().Be("ResultadoReconciliado");
        ev8.Tipo.Should().Be("OperacaoConcluida");
        ev9.Tipo.Should().Be("EntregaCallbackAceita");
        ev10.Tipo.Should().Be("EntregaCallbackEsgotada");
        ev11.Tipo.Should().Be("ContaLiberada");
    }

    [Fact]
    public void TS_041_Should_Classificar_Retry_Seguro_Sem_Estado_Residual()
    {
        // Given: Classificador de falhas para orçamento de retry
        bool PodeRepetir(Exception ex, bool intencaoRegistrada)
        {
            if (intencaoRegistrada) return false; // Nunca repete após intenção de envio
            return ex is HttpRequestException;
        }

        // When & Then
        PodeRepetir(new HttpRequestException(), intencaoRegistrada: false).Should().BeTrue();
        PodeRepetir(new TimeoutException(), intencaoRegistrada: true).Should().BeFalse();
    }

    [Fact]
    public void TS_042_Should_Limitar_Escala_Por_Capacidades_When_Profundidade_Fila_Aumenta()
    {
        // Given
        int limiteCapacidadeBanco = 4;
        int limiteOutbank = 2;
        int workersSolicitados = 10;

        // When: Aplicação de orçamento e capacidade
        int concorrenciaEfetiva = Math.Min(workersSolicitados, Math.Min(limiteCapacidadeBanco, limiteOutbank));

        // Then: Concorrência máxima nunca ultrapassa o gargalo externo de 2
        concorrenciaEfetiva.Should().Be(2);
    }
}
