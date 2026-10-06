using FluentAssertions;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Exceptions;
using Pix.Processing.Domain.ValueObjects;
using Xunit;

namespace Unit;

public class DomainPixTests
{
    [Fact]
    public void PIX_001_Should_Validar_EndToEndId_When_Formato_Bacen_Possui_32_Caracteres()
    {
        // Given: String de 32 caracteres compatível com a especificação SPI
        var valorValido = "E12345678202610051200ABCDEF12345";

        // When: Instanciar EndToEndId.Criar
        var endToEndId = EndToEndId.Criar(valorValido);

        // Then: Objeto EndToEndId válido criado com sucesso
        endToEndId.Valor.Should().Be(valorValido);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("E123")] // curto
    [InlineData("E12345678202610051200ABCDEF123457890")] // longo
    public void PIX_002_Should_Rejeitar_EndToEndId_When_String_Nula_Ou_Tamanho_Diferente_De_32(string? valorInvalido)
    {
        // Given & When: Tentar instanciar EndToEndId.Criar com valor inválido
        var act = () => EndToEndId.Criar(valorInvalido!);

        // Then: Lançar DomainException impedindo a criação
        act.Should().Throw<DomainException>()
            .WithMessage("*32*");
    }

    [Fact]
    public void PIX_003_Should_Validar_Quantia_Monetaria_When_Valor_Positivo()
    {
        // Given: Valor decimal positivo com duas casas decimais
        var quantia = 150.50m;

        // When: Instanciar Money.De
        var money = Money.De(quantia);

        // Then: Objeto Money criado com moeda padrão BRL
        money.Quantia.Should().Be(quantia);
        money.Moeda.Should().Be("BRL");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-150.50)]
    public void PIX_004_Should_Rejeitar_Quantia_Monetaria_When_Valor_Zero_Ou_Negativo(decimal valorInvalido)
    {
        // Given & When: Tentar instanciar Money.De com zero ou negativo
        var act = () => Money.De(valorInvalido);

        // Then: Lançar DomainException
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void PIX_005_Should_Compor_Chave_De_Idempotencia_When_Parametros_Validos()
    {
        // Given: Parâmetros válidos
        var clienteId = new ClienteId("cli-1");
        var tipoOperacao = "CashOut";
        var chave = "k-10";

        // When: Instanciar IdempotencyKey
        var idempotencyKey = new IdempotencyKey(clienteId, tipoOperacao, chave);

        // Then: ChaveComposta deve formatar clienteId:tipo:chave em minúsculo na operação
        idempotencyKey.ChaveComposta.Should().Be("cli-1:cashout:k-10");
    }

    [Fact]
    public void PIX_006_Should_Iniciar_PedidoPix_Em_Estado_Aceito_When_Dados_Validos()
    {
        // Given: Dados de comando válidos
        var pedidoId = Guid.NewGuid();
        var clienteId = new ClienteId("cli-1");
        var contaId = new ContaId("cnt-1");
        var endToEndId = EndToEndId.Criar("E12345678202610051200ABCDEF12345");
        var valor = Money.De(200.00m);

        // When: Instanciar PedidoPix
        var pedido = new PedidoPix(pedidoId, clienteId, contaId, TipoOperacaoPix.CashOut, endToEndId, valor);

        // Then: Estado inicial é Aceito
        pedido.Estado.Should().Be(EstadoPedidoPix.Aceito);
        pedido.Versao.Should().Be(1);
    }

    [Fact]
    public void PIX_007_Should_Bloquear_Transicao_Terminal_When_Pedido_Ja_Confirmado()
    {
        // Given: PedidoPix já confirmado com evidência SPI
        var pedido = new PedidoPix(
            Guid.NewGuid(),
            new ClienteId("cli-1"),
            new ContaId("cnt-1"),
            TipoOperacaoPix.CashOut,
            EndToEndId.Criar("E12345678202610051200ABCDEF12345"),
            Money.De(100.00m));

        var agora = DateTimeOffset.UtcNow;
        var evidencia = new ExternalEvidence("SPI", "CONFIRMADO", "BACEN", "spi-ref-123", agora, true);

        pedido.IniciarProcessamento("worker-1", agora);
        pedido.Confirmar(evidencia, versaoEsperada: 2, ownerEsperado: "worker-1", agora);

        pedido.Estado.Should().Be(EstadoPedidoPix.Confirmado);

        // When: Tentar chamar RejeitarDefinitivo ou transicionar terminalmente de novo
        var act = () => pedido.RejeitarDefinitivo(evidencia, versaoEsperada: 3, ownerEsperado: "worker-1", agora);

        // Then: Deve bloquear transição conflitante mantendo estado original intacto
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*já está Confirmado*");
        pedido.Estado.Should().Be(EstadoPedidoPix.Confirmado);
    }
}
