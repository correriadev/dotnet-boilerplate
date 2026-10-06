using Pix.Billing.Domain;

namespace Pix.Billing.Application;

public record CobrancaJobItem(
    string JobId,
    CobrancaId CobrancaId,
    Guid PedidoId,
    decimal Valor,
    string Status,
    DateTimeOffset CriadoEm);

public interface ICobrancaJobStore
{
    Task CriarCobrancaEJobAsync(CobrancaPix cobranca, string jobId, CancellationToken ct = default);
    Task<bool> CriarCobrancaComJobAsync(CobrancaPix cobranca, string jobId, CancellationToken ct = default);
    Task<CobrancaPix?> ObterPorIdAsync(CobrancaId id, CancellationToken ct = default);
    Task ConcluirJobComResultadoAsync(CobrancaPix cobranca, string jobId, string tipoEventoOutbox, CancellationToken ct = default);
}

public interface IQrGeneratorAcl
{
    Task<(bool Sucesso, string ArtefatoOuMotivo)> GerarQrCodeAsync(CobrancaId id, decimal valor, CancellationToken ct = default);
}

public class RequestBillingUseCase
{
    private readonly ICobrancaJobStore _jobStore;

    public RequestBillingUseCase(ICobrancaJobStore jobStore)
    {
        _jobStore = jobStore;
    }

    public async Task<CobrancaPix> SolicitarAsync(string cobrancaRef, Guid pedidoId, decimal valor, CancellationToken ct = default)
    {
        var cobrancaId = new CobrancaId(cobrancaRef);
        var cobranca = new CobrancaPix(cobrancaId, pedidoId, valor);
        var jobId = Guid.NewGuid().ToString();

        // TX-04: persiste Inbox + Cobranca + job atomicamente (INV-10)
        await _jobStore.CriarCobrancaEJobAsync(cobranca, jobId, ct);
        return cobranca;
    }
}

public class GenerateBillingUseCase
{
    private readonly ICobrancaJobStore _jobStore;
    private readonly IQrGeneratorAcl _qrGeneratorAcl;

    public GenerateBillingUseCase(ICobrancaJobStore jobStore, IQrGeneratorAcl qrGeneratorAcl)
    {
        _jobStore = jobStore;
        _qrGeneratorAcl = qrGeneratorAcl;
    }

    public async Task ExecutarJobAsync(string jobId, CobrancaId cobrancaId, CancellationToken ct = default)
    {
        var cobranca = await _jobStore.ObterPorIdAsync(cobrancaId, ct);
        if (cobranca == null) return;

        cobranca.IniciarGeracao(DateTimeOffset.UtcNow);

        var (sucesso, artefatoOuMotivo) = await _qrGeneratorAcl.GerarQrCodeAsync(cobrancaId, cobranca.Valor, ct);

        if (sucesso)
        {
            cobranca.Disponibilizar(artefatoOuMotivo, DateTimeOffset.UtcNow);
            await _jobStore.ConcluirJobComResultadoAsync(cobranca, jobId, "CobrancaDisponibilizada", ct);
        }
        else
        {
            cobranca.Recusar(artefatoOuMotivo, DateTimeOffset.UtcNow);
            await _jobStore.ConcluirJobComResultadoAsync(cobranca, jobId, "GeracaoCobrancaRecusada", ct);
        }
    }
}
