using FluentAssertions;
using Pix.Billing.Domain;
using Pix.Callbacks.Domain;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;
using Xunit;

namespace Unit;

public class DomainTests
{
    [Fact]
    public void TS_001_Should_Criar_PedidoPix_Em_Aceito_When_Solicitacao_Valida()
    {
        // Given
        var clienteId = new ClienteId("cliente-A");
        var contaId = new ContaId("conta-A");
        var endToEndId = new EndToEndId("E2E12345678901234567890123456789");
        var valor = new Money(150.50m, "BRL");

        // When
        var pedido = new PedidoPix(Guid.NewGuid(), clienteId, contaId, TipoOperacaoPix.CashOut, endToEndId, valor);

        // Then
        pedido.Estado.Should().Be(EstadoPedidoPix.Aceito);
        pedido.Versao.Should().Be(1);
        pedido.EndToEndId.Should().Be(endToEndId);
        pedido.Valor.Quantia.Should().Be(150.50m);
    }

    [Fact]
    public void TS_006_Should_Criar_Coordenacao_Com_Proxima_Sequencia_Positiva()
    {
        // Given
        var contaId = new ContaId("conta-A");

        // When
        var coord = new CoordenacaoConta(contaId, 1);
        var seq = coord.AtribuirProximaSequencia(DateTimeOffset.UtcNow);

        // Then
        seq.Numero.Should().Be(1);
        coord.ProximaSequenciaAtribuivel.Should().Be(2);
        coord.Estado.Should().Be(EstadoCoordenacao.Livre);
    }

    [Fact]
    public void TS_007_Should_Impedir_Comando_Dependente_When_Conta_Esta_Bloqueada()
    {
        // Given
        var coord = new CoordenacaoConta(new ContaId("conta-A"), 1);
        coord.AtribuirProximaSequencia(DateTimeOffset.UtcNow);
        coord.Bloquear(1, "Incerteza na seq 1", DateTimeOffset.UtcNow);

        // When & Then
        coord.Estado.Should().Be(EstadoCoordenacao.Bloqueada);
        var proximaSeq = coord.ProximaSequenciaExecutavel;
        proximaSeq.Should().Be(1); // Não avança para seq 2 enquanto bloqueada
    }

    [Fact]
    public void TS_011_Should_Confirmar_Ou_Rejeitar_Definitivamente_When_Evidencia_Conclusiva_Valida()
    {
        // Given
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(50m));
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        var evidencia = new ExternalEvidence("EnvioPix", "Liquidacao", "Outbank", "REF-123", DateTimeOffset.UtcNow, sucesso: true);

        // When
        pedido.Confirmar(evidencia, 2, "worker-1", DateTimeOffset.UtcNow);

        // Then
        pedido.Estado.Should().Be(EstadoPedidoPix.Confirmado);
        pedido.UltimaEvidencia.Should().Be(evidencia);
        pedido.Versao.Should().Be(3);
    }

    [Fact]
    public void TS_012_Should_Voltar_Ao_Aceite_When_Falha_Comprovadamente_Anterior_A_Intencao()
    {
        // Given
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(50m));
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);

        // When
        pedido.ReverterParaAceitoAposFalhaPreEnvio("Falha de rede pré-envio", DateTimeOffset.UtcNow);

        // Then
        pedido.Estado.Should().Be(EstadoPedidoPix.Aceito);
        pedido.OwnerVigente.Should().BeNull();
        pedido.Versao.Should().Be(3);
    }

    [Fact]
    public void TS_016_Should_Permitir_Retorno_Excepcional_Ao_Processamento_Com_Prova_Ausencia_Homologada()
    {
        // Given
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(50m));
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        pedido.MarcarResultadoIncerto("Timeout", null, DateTimeOffset.UtcNow);
        pedido.EncaminharParaTriagem("Investigação", DateTimeOffset.UtcNow);

        var provaAusencia = new ExternalEvidence("Auditoria", "ProvaAusencia", "Outbank-Homolog", "PROOF-001", DateTimeOffset.UtcNow, sucesso: false);

        // When
        pedido.RetornarExcepcionalAoProcessamento("worker-2", provaAusencia, DateTimeOffset.UtcNow);

        // Then
        pedido.Estado.Should().Be(EstadoPedidoPix.EmProcessamento);
        pedido.OwnerVigente.Should().Be("worker-2");
        pedido.UltimaEvidencia.Should().Be(provaAusencia);
    }

    [Fact]
    public void TS_017_Should_Preservar_Terminal_When_Evidencia_Nova_Conflita_Com_Conclusao_Existente()
    {
        // Given
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(50m));
        pedido.IniciarProcessamento("worker-1", DateTimeOffset.UtcNow);
        var evSucesso = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF-OK", DateTimeOffset.UtcNow, sucesso: true);
        pedido.Confirmar(evSucesso, 2, "worker-1", DateTimeOffset.UtcNow);

        // When & Then: tentativa de sobrescrever terminal com recusa é rejeitada
        var evRecusa = new ExternalEvidence("EnvioPix", "Recusa", "Outbank", "REF-NOK", DateTimeOffset.UtcNow, sucesso: false);
        var act = () => pedido.RejeitarDefinitivo(evRecusa, 3, "worker-1", DateTimeOffset.UtcNow);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*não pode ser sobrescrito*");
    }

    [Fact]
    public void TS_018_Should_Recusar_Escritor_Obsoleto_When_Owner_Ou_Versao_Mudou()
    {
        // Given
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("c1"), new ContaId("cnt1"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(50m));
        pedido.IniciarProcessamento("worker-novo", DateTimeOffset.UtcNow);
        var ev = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF", DateTimeOffset.UtcNow, sucesso: true);

        // When & Then: versão ou owner desatualizados provocam exceção de concorrência
        var act = () => pedido.Confirmar(ev, 99, "worker-antigo", DateTimeOffset.UtcNow);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Concorrência detectada*");
    }

    [Fact]
    public void TS_019_Should_Manter_Outras_Razões_De_Bloqueio_When_Impedimento_Corrente_Resolvido()
    {
        // Given
        var coord = new CoordenacaoConta(new ContaId("conta-A"), 1);
        coord.Bloquear(1, "Incerteza seq 1", DateTimeOffset.UtcNow);
        coord.Bloquear(2, "Incerteza seq 2", DateTimeOffset.UtcNow);

        // When: Desbloqueia impedimento da seq 1
        coord.Desbloquear(1, "Incerteza seq 1", DateTimeOffset.UtcNow);

        // Then: Continua bloqueada pela outra razão (seq 2)
        coord.Estado.Should().Be(EstadoCoordenacao.Bloqueada);
    }

    [Fact]
    public void TS_020_Should_Criar_CobrancaPix_Solicitada_When_Pedido_Qr_Puro_Valido()
    {
        // Given & When
        var cobranca = new CobrancaPix(new CobrancaId("COB-123"), Guid.NewGuid(), 200m);

        // Then
        cobranca.Estado.Should().Be(EstadoCobranca.Solicitada);
        cobranca.Valor.Should().Be(200m);
    }

    [Fact]
    public void TS_021_Should_Disponibilizar_Ou_Recusar_CobrancaPix_When_Job_Recebe_Resultado()
    {
        // Given
        var cobranca1 = new CobrancaPix(new CobrancaId("COB-1"), Guid.NewGuid(), 100m);
        cobranca1.IniciarGeracao(DateTimeOffset.UtcNow);
        var cobranca2 = new CobrancaPix(new CobrancaId("COB-2"), Guid.NewGuid(), 100m);
        cobranca2.IniciarGeracao(DateTimeOffset.UtcNow);

        // When
        cobranca1.Disponibilizar("EMV:00020126...", DateTimeOffset.UtcNow);
        cobranca2.Recusar("Chave Pix de destino inexistente", DateTimeOffset.UtcNow);

        // Then
        cobranca1.Estado.Should().Be(EstadoCobranca.Disponivel);
        cobranca1.ArtefatoQr.Should().NotBeNullOrEmpty();

        cobranca2.Estado.Should().Be(EstadoCobranca.Recusada);
        cobranca2.MotivoRecusa.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TS_022_Should_Preservar_Geracao_Incerta_When_Integracao_Qr_Opcional_Perde_Resposta()
    {
        // Given
        var cobranca = new CobrancaPix(new CobrancaId("COB-3"), Guid.NewGuid(), 50m);
        cobranca.IniciarGeracao(DateTimeOffset.UtcNow);

        // When: perda de resposta
        cobranca.MarcarGeracaoIncerta("Timeout no gerador QR", DateTimeOffset.UtcNow);

        // Then
        cobranca.Estado.Should().Be(EstadoCobranca.GeracaoIncerta);
    }

    [Fact]
    public void TS_024_Should_Criar_Entrega_Callback_Pendente_Com_Destinatario()
    {
        // Given & When
        var entregaId = DeliveryId.Novo();
        var entrega = new CallbackDelivery(entregaId, "EV-100", "DEST-01", "{\"evento\":\"concluido\"}", 3);

        // Then
        entrega.Estado.Should().Be(EstadoEntrega.Pendente);
        entrega.Tentativas.Should().Be(0);
        entrega.MaxTentativas.Should().Be(3);
        entrega.Id.Should().Be(entregaId);
    }

    [Fact]
    public void TS_025_Should_Registrar_Callback_Aceito_When_Destinatario_Confirma()
    {
        // Given
        var entrega = new CallbackDelivery(DeliveryId.Novo(), "EV-1", "D-1", "{}", 3);
        entrega.IniciarEnvio(DateTimeOffset.UtcNow);

        // When
        entrega.RegistrarAceite(DateTimeOffset.UtcNow);

        // Then
        entrega.Estado.Should().Be(EstadoEntrega.Aceita);
    }

    [Fact]
    public void TS_026_Should_Agendar_Retry_E_Esgotamento_When_Callback_Falha_Sob_Orcamento_Finito()
    {
        // Given
        var entrega = new CallbackDelivery(DeliveryId.Novo(), "EV-1", "D-1", "{}", maxTentativas: 2);
        entrega.IniciarEnvio(DateTimeOffset.UtcNow);

        // When 1: Primeira falha
        entrega.RegistrarFalha(DateTimeOffset.UtcNow);

        // Then 1: Aguardando retry
        entrega.Estado.Should().Be(EstadoEntrega.AguardandoRetry);
        entrega.Tentativas.Should().Be(1);

        // When 2: Segunda falha
        entrega.IniciarEnvio(DateTimeOffset.UtcNow);
        entrega.RegistrarFalha(DateTimeOffset.UtcNow);

        // Then 2: Esgotada
        entrega.Estado.Should().Be(EstadoEntrega.Esgotada);
    }

    [Fact]
    public void TS_027_Should_Recusar_Transicoes_Nao_Permitidas()
    {
        // Given
        var pedido = new PedidoPix(Guid.NewGuid(), new ClienteId("cli"), new ContaId("cnt"), TipoOperacaoPix.CashOut, new EndToEndId("E1"), new Money(10m));

        // When & Then: Confirmar diretamente sem EmProcessamento
        var ev = new ExternalEvidence("EnvioPix", "Sucesso", "Outbank", "REF", DateTimeOffset.UtcNow, sucesso: true);
        var act = () => pedido.Confirmar(ev, 1, "worker", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Transição inválida para Confirmado*");
    }

    [Fact]
    public void TS_028_Should_Validar_E_Comparar_Identidades_Canonicas()
    {
        // Given & When
        var c1 = new ClienteId("cli-123");
        var c2 = new ClienteId("cli-123");
        var c3 = new ClienteId("cli-999");

        // Then
        c1.Should().Be(c2);
        c1.Should().NotBe(c3);
        c1.GetHashCode().Should().Be(c2.GetHashCode());

        var act = () => new ClienteId("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_029_Should_Respeitar_Contrato_Da_Chave_E_RequestCode()
    {
        // Given
        var clienteId = new ClienteId("cli-1");
        var key = new IdempotencyKey(clienteId, "CashOut", "key-12345");
        var reqCode = new RequestCode("REQ-ABC-999");

        // Then
        key.ChaveComposta.Should().Be("cli-1:cashout:key-12345");
        reqCode.Valor.Should().Be("REQ-ABC-999");

        var act = () => new IdempotencyKey(clienteId, "", "k");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_030_Should_Canonicalizar_Fingerprint_Sem_Cultura()
    {
        // Given
        var payload1 = new Dictionary<string, object?> { ["valor"] = 150.50m, ["conta"] = "C1" };
        var payload2 = new Dictionary<string, object?> { ["conta"] = "C1", ["valor"] = 150.50m };

        // When
        var fp1 = Fingerprint.GerarDePayload("v1", payload1);
        var fp2 = Fingerprint.GerarDePayload("v1", payload2);

        // Then: Mesma ordenação estável e representação invariante de cultura
        fp1.Hash.Should().Be(fp2.Hash);
        fp1.Versao.Should().Be("v1");
    }

    [Fact]
    public void TS_031_Should_Validar_Money_Exato()
    {
        // Given & When
        var m1 = new Money(0.10m);
        var m2 = new Money(0.20m);
        var soma = m1 + m2;

        // Then
        soma.Quantia.Should().Be(0.30m);
        soma.Moeda.Should().Be("BRL");

        var act1 = () => new Money(-1m);
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new Money(10.123m); // Mais de 2 casas decimais
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_032_Should_Validar_AccountSequence_Monotonica()
    {
        // Given
        var conta = new ContaId("C1");
        var seq1 = new AccountSequence(conta, 1);
        var seq2 = seq1.Proximo();

        // Then
        seq2.Numero.Should().Be(2);
        seq2.ContaId.Should().Be(conta);

        var act = () => new AccountSequence(conta, 0);
        act.Should().Throw<ArgumentException>();
    }
}
