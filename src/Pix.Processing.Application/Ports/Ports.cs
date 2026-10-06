using Pix.Processing.Application.Models;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Ports;

public interface IAcceptanceStore
{
    Task<AceiteResultado> ConfirmarAceiteAsync(
        PedidoPix pedido,
        IdempotencyKey chave,
        Fingerprint fingerprint,
        Func<Guid, AceiteOriginal> factoryAceite,
        bool ehFinanceiro,
        CancellationToken ct = default);

    Task<AceiteOriginal?> ObterAceitePorChaveAsync(IdempotencyKey chave, CancellationToken ct = default);
}

public interface IExecutionStore
{
    Task<AquisicaoResultado> AdquirirExecucaoAsync(
        EndToEndId endToEndId,
        Guid pedidoId,
        ContaId contaId,
        long sequencia,
        string owner,
        long generation,
        DateTimeOffset expiresAt,
        CancellationToken ct = default);

    Task RegistrarIntencaoAsync(
        EndToEndId endToEndId,
        string owner,
        int versaoEsperada,
        CancellationToken ct = default);

    Task<bool> ConcluirExecucaoAsync(
        EndToEndId endToEndId,
        Guid pedidoId,
        ExternalEvidence evidencia,
        EstadoPedidoPix estadoFinal,
        string owner,
        int versaoEsperada,
        DominioEvento outboxEvento,
        CancellationToken ct = default);

    Task<RegistroExecucao?> ObterEstadoAsync(EndToEndId endToEndId, CancellationToken ct = default);
}

public interface IAdmissionLimiter
{
    Task<AdmissaoResultado> VerificarAdmissaoAsync(ClienteId clienteId, string tipoOperacao, CancellationToken ct = default);
}

public interface IOutbankAcl
{
    Task<ExternalEvidence> ExecutarEnvioPixAsync(EndToEndId endToEndId, Money valor, ContaId origem, CancellationToken ct = default);
    Task<ExternalEvidence?> ConsultarPixAsync(EndToEndId endToEndId, CancellationToken ct = default);
}

public interface IFinancialCache
{
    Task<FinancialSnapshot?> ObterSnapshotAsync(ContaId contaId, CancellationToken ct = default);
    Task<bool> PreencherCasAsync(ContaId contaId, FinancialSnapshot snapshot, CacheGeneration geracaoEsperada, CancellationToken ct = default);
    Task IncrementarGeracaoERemoverAsync(ContaId contaId, CancellationToken ct = default);
    Task<CacheGeneration> ObterGeracaoAtualAsync(ContaId contaId, CancellationToken ct = default);
}

public interface IAutorizacaoPort
{
    Task<bool> AutorizarAsync(ClienteId clienteId, ContaId contaId, string acao, CancellationToken ct = default);
}

public interface IOutboxStore
{
    Task<IReadOnlyList<DominioEvento>> AdquirirLoteAsync(int batchSize, string owner, CancellationToken ct = default);
    Task MarcarPublicadoAsync(string eventId, string owner, CancellationToken ct = default);
    Task MarcarPublicadoAsync(string eventId, string owner, long generation, CancellationToken ct = default);
}

public interface IInboxStore
{
    Task<bool> RegistrarInboxEPendenciaAsync(string eventId, PendingCommandItem comando, CancellationToken ct = default);
    Task<IReadOnlyList<PendingCommandItem>> ObterPendenciasElegiveisAsync(ContaId contaId, long proximaSequencia, CancellationToken ct = default);
    Task MarcarComandoResolvidoAsync(string eventId, CancellationToken ct = default);
}
