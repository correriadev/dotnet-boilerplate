using Pix.Callbacks.Domain;

namespace Pix.Callbacks.Application;

public interface ICallbackDeliveryStore
{
    Task<bool> AgendarEntregaAsync(CallbackDelivery entrega, CancellationToken ct = default);
    Task<IReadOnlyList<CallbackDelivery>> ObterEntregasPendentesAsync(int limite, CancellationToken ct = default);
    Task AtualizarEntregaAsync(CallbackDelivery entrega, CancellationToken ct = default);
}

public interface ISafeCallbackTransport
{
    Task<bool> EnviarAsync(CallbackDestination destino, string payloadJson, CancellationToken ct = default);
}

public class ScheduleDeliveryUseCase
{
    private readonly ICallbackDeliveryStore _store;

    public ScheduleDeliveryUseCase(ICallbackDeliveryStore store)
    {
        _store = store;
    }

    public async Task<CallbackDelivery> AgendarAsync(
        string eventoConclusaoId,
        string destinatarioId,
        string payloadJson,
        int maxTentativas = 3,
        CancellationToken ct = default)
    {
        var entregaId = DeliveryId.Novo();
        var entrega = new CallbackDelivery(entregaId, eventoConclusaoId, destinatarioId, payloadJson, maxTentativas);

        // TX-05: Inbox + Entrega UNIQUE(eventoConclusaoId, destinatarioId) antes do Complete
        await _store.AgendarEntregaAsync(entrega, ct);
        return entrega;
    }
}

public class DeliverCallbackUseCase
{
    private readonly ICallbackDeliveryStore _store;
    private readonly ISafeCallbackTransport _transport;

    public DeliverCallbackUseCase(ICallbackDeliveryStore store, ISafeCallbackTransport transport)
    {
        _store = store;
        _transport = transport;
    }

    public async Task ExecutarEntregaAsync(CallbackDelivery entrega, CallbackDestination destino, CancellationToken ct = default)
    {
        entrega.IniciarEnvio(DateTimeOffset.UtcNow);

        // HTTP fora de transação relacional (INV-14, D-11)
        bool sucesso = false;
        try
        {
            sucesso = await _transport.EnviarAsync(destino, entrega.PayloadJson, ct);
        }
        catch
        {
            sucesso = false;
        }

        if (sucesso)
        {
            entrega.RegistrarAceite(DateTimeOffset.UtcNow);
        }
        else
        {
            entrega.RegistrarFalha(DateTimeOffset.UtcNow);
        }

        await _store.AtualizarEntregaAsync(entrega, ct);
    }
}
