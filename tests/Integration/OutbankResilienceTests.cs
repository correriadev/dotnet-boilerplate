using System.Diagnostics;
using FluentAssertions;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure.Adapters;
using Xunit;

namespace Integration;

public class OutbankResilienceTests
{
    [Fact]
    public async Task OUT_021_Should_Disparar_Timeout_De_2_Segundos_When_Chamada_Spi_Demora()
    {
        // Given: Outbank ACL configurado com política de timeout estrito de 2 segundos
        var adapter = new OutbankAclAdapter();
        adapter.ConfigurarTimeoutSpi(TimeSpan.FromSeconds(2));
        adapter.ConfigurarDelaySimulado(TimeSpan.FromSeconds(5)); // Delay que excede 2s

        var endToEndId = EndToEndId.Criar("E12345678202610051200ABCDEF12345");
        var valor = Money.De(100.00m);
        var origem = new ContaId("cnt-origem-01");

        var sw = Stopwatch.StartNew();

        // When: Executar envio com delay longo
        Func<Task> act = async () => await adapter.ExecutarEnvioPixAsync(endToEndId, valor, origem);

        // Then: Dispara TimeoutException em aproximadamente 2 segundos sem esperar os 5 segundos completos
        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*2 segundos*");

        sw.Stop();
        sw.Elapsed.TotalSeconds.Should().BeInRange(1.8, 3.5, "Timeout deve disparar em torno de 2 segundos");
    }
}
