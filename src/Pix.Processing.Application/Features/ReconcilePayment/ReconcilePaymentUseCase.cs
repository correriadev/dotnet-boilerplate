using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Features.ReconcilePayment;

public class ReconcilePaymentUseCase
{
    private readonly IExecutionStore _executionStore;
    private readonly IOutbankAcl _outbankAcl;

    public ReconcilePaymentUseCase(IExecutionStore executionStore, IOutbankAcl outbankAcl)
    {
        _executionStore = executionStore;
        _outbankAcl = outbankAcl;
    }

    public async Task<bool> ReconciliarAsync(
        EndToEndId endToEndId,
        Guid pedidoId,
        ContaId contaId,
        string owner,
        int versaoEsperada,
        CancellationToken ct = default)
    {
        // 1. Consultar evidência autoritativa no Outbank (INV-07)
        var evidencia = await _outbankAcl.ConsultarPixAsync(endToEndId, ct);

        if (evidencia == null || string.IsNullOrWhiteSpace(evidencia.Referencia))
        {
            // Evidência insuficiente: "não encontrado" isolado não prova ausência (INV-07).
            // Permanece em Triagem / Incerteza conservadora.
            return false;
        }

        // 2. Resolver resultado usando versão esperada e ownership vigente (INV-08, INV-12)
        var estadoFinal = evidencia.Sucesso ? EstadoPedidoPix.Confirmado : EstadoPedidoPix.RejeitadoDefinitivo;

        var eventoConclusao = new OperacaoConcluidaEvento(
            Guid.NewGuid().ToString(),
            pedidoId.ToString(),
            pedidoId,
            endToEndId,
            estadoFinal,
            evidencia.Sucesso ? "RECONCILIADO_CONFIRMADO" : "RECONCILIADO_REJEITADO",
            contaId,
            DateTimeOffset.UtcNow);

        return await _executionStore.ConcluirExecucaoAsync(
            endToEndId,
            pedidoId,
            evidencia,
            estadoFinal,
            owner,
            versaoEsperada,
            eventoConclusao,
            ct);
    }
}
