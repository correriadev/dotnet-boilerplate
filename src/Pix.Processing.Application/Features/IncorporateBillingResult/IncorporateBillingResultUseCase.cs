using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Features.IncorporateBillingResult;

public class IncorporateBillingResultUseCase
{
    private readonly IExecutionStore _executionStore;

    public IncorporateBillingResultUseCase(IExecutionStore executionStore)
    {
        _executionStore = executionStore;
    }

    public async Task<bool> IncorporarAsync(
        Guid pedidoId,
        bool geracaoSucesso,
        string cobrancaId,
        string? artefatoOuMotivo,
        string owner,
        CancellationToken ct = default)
    {
        var estadoFinal = geracaoSucesso ? EstadoPedidoPix.Confirmado : EstadoPedidoPix.RejeitadoDefinitivo;
        var evidencia = new ExternalEvidence(
            "GeracaoCobranca",
            geracaoSucesso ? "ArtefatoDisponivel" : "RecusaHomologada",
            "Pix.Billing",
            cobrancaId,
            DateTimeOffset.UtcNow,
            geracaoSucesso);

        var fakeEndToEndId = new EndToEndId($"E2E-COBRANCA-{cobrancaId}");

        var eventoConclusao = new OperacaoConcluidaEvento(
            Guid.NewGuid().ToString(),
            pedidoId.ToString(),
            pedidoId,
            null,
            estadoFinal,
            geracaoSucesso ? "COBRANCA_DISPONIVEL" : "COBRANCA_RECUSADA",
            null,
            DateTimeOffset.UtcNow);

        // Incorpora atomicamente na transação própria do Processamento (INV-11, INV-12)
        return await _executionStore.ConcluirExecucaoAsync(
            fakeEndToEndId,
            pedidoId,
            evidencia,
            estadoFinal,
            owner,
            1, // Versão inicial do pedido em aceite
            eventoConclusao,
            ct);
    }
}
