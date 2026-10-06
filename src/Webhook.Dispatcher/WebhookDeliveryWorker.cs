using Pix.Callbacks.Application;
using Pix.Callbacks.Domain;

namespace Webhook.Dispatcher;

public class WebhookDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookDeliveryWorker> _logger;

    public WebhookDeliveryWorker(IServiceProvider serviceProvider, ILogger<WebhookDeliveryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Webhook Dispatcher iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ICallbackDeliveryStore>();
                var useCase = scope.ServiceProvider.GetRequiredService<DeliverCallbackUseCase>();

                var pendentes = await store.ObterEntregasPendentesAsync(10, stoppingToken);

                foreach (var entrega in pendentes)
                {
                    _logger.LogInformation("Disparando callback para entrega {EntregaId}...", entrega.Id);
                    // Destino homologado padrão HTTPS (INV-15)
                    var destino = new CallbackDestination("dest-01", "https://api.empresa-cliente.com/webhook/pix", "secret-key-123");
                    await useCase.ExecutarEntregaAsync(entrega, destino, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no loop do WebhookDeliveryWorker.");
            }

            await Task.Delay(3000, stoppingToken);
        }
    }
}
