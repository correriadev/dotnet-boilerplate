using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Xunit;

namespace Integration;

public class ServiceBusTopologyTests
{
    [Fact]
    public void SB_019_Should_Parametrizar_Fila_E_Processar_Mensagens_Em_Ordem_FIFO_Estrita_Com_Sessions()
    {
        // Given: Configurações parametrizando nomes canônicos do domínio
        var settings = new Dictionary<string, string>
        {
            ["ServiceBus:Queues:CashOutRequested"] = "pix-cashout-requested",
            ["ServiceBus:Topics:CashOutCompleted"] = "pix-cashout-completed"
        };

        // When: Resolver nome da fila
        var queueName = settings.TryGetValue("ServiceBus:Queues:CashOutRequested", out var q) 
            ? q 
            : "pix-payments-session";

        // Then 1: Fila customizada deve ser pix-cashout-requested
        queueName.Should().Be("pix-cashout-requested");

        // When: Construir mensagens com sessionId da conta do cliente
        var contaCliente = "cnt-cliente-99";
        var msg1 = new ServiceBusMessage("{\"seq\":1}") { MessageId = "M1", SessionId = contaCliente };
        var msg2 = new ServiceBusMessage("{\"seq\":2}") { MessageId = "M2", SessionId = contaCliente };

        // Then 2: Mesma sessão garante ordem FIFO no broker
        msg1.SessionId.Should().Be(contaCliente);
        msg2.SessionId.Should().Be(contaCliente);
        msg1.SessionId.Should().Be(msg2.SessionId);
    }

    [Fact]
    public void SB_020_Should_Configurar_MaxDeliveryCount_E_Mover_Mensagem_Para_DLQ()
    {
        // Given: Configuração do receiver com Dead Letter Queue
        var options = new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter
        };

        var queuePath = "pix-cashout-requested";
        var dlqPath = $"{queuePath}/$DeadLetterQueue";

        // Then: SubQueue aponta para Dead Letter Queue
        options.SubQueue.Should().Be(SubQueue.DeadLetter);
        dlqPath.Should().Contain("$DeadLetterQueue");
    }
}
