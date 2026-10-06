using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Domain.Events;

public abstract record DominioEvento(
    string EventId,
    string Tipo,
    string SchemaVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId)
{
    public long Generation { get; init; } = 0;
}

public record PedidoFinanceiroAceitoEvento(
    string EventId,
    string CorrelationId,
    Guid PedidoId,
    ContaId ContaId,
    AccountSequence Sequencia,
    EndToEndId EndToEndId,
    TipoOperacaoPix TipoOperacao,
    Money Valor,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "PedidoFinanceiroAceito", "1.0", OccurredAt, CorrelationId);

public record GeracaoCobrancaSolicitadaEvento(
    string EventId,
    string CorrelationId,
    Guid PedidoId,
    string CobrancaRef,
    Money Valor,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "GeracaoCobrancaSolicitada", "1.0", OccurredAt, CorrelationId);

public record CobrancaDisponibilizadaEvento(
    string EventId,
    string CorrelationId,
    Guid PedidoId,
    string CobrancaId,
    string ArtefatoQr,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "CobrancaDisponibilizada", "1.0", OccurredAt, CorrelationId);

public record GeracaoCobrancaRecusadaEvento(
    string EventId,
    string CorrelationId,
    Guid PedidoId,
    string CobrancaId,
    string MotivoRecusa,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "GeracaoCobrancaRecusada", "1.0", OccurredAt, CorrelationId);

public record ResultadoFinanceiroIncertoEvento(
    string EventId,
    string CorrelationId,
    Guid PedidoId,
    ContaId ContaId,
    EndToEndId EndToEndId,
    string Motivo,
    string EvidenciaRef,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "ResultadoFinanceiroIncerto", "1.0", OccurredAt, CorrelationId);

public record ContaBloqueadaEvento(
    string EventId,
    string CorrelationId,
    ContaId ContaId,
    long SequenciaImpedida,
    string Motivo,
    Guid PedidoId,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "ContaBloqueada", "1.0", OccurredAt, CorrelationId);

public record ResultadoReconciliadoEvento(
    string EventId,
    string CorrelationId,
    Guid PedidoId,
    string EvidenciaRef,
    string Decisao,
    int Versao,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "ResultadoReconciliado", "1.0", OccurredAt, CorrelationId);

public record OperacaoConcluidaEvento(
    string EventId,
    string CorrelationId,
    Guid PedidoId,
    EndToEndId? EndToEndId,
    EstadoPedidoPix EstadoFinal,
    string Resultado,
    ContaId? ContaId,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "OperacaoConcluida", "1.0", OccurredAt, CorrelationId);

public record EntregaCallbackAceitaEvento(
    string EventId,
    string CorrelationId,
    Guid EntregaId,
    string EventoConclusaoId,
    string DestinoRef,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "EntregaCallbackAceita", "1.0", OccurredAt, CorrelationId);

public record EntregaCallbackEsgotadaEvento(
    string EventId,
    string CorrelationId,
    Guid EntregaId,
    string EventoConclusaoId,
    string Motivo,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "EntregaCallbackEsgotada", "1.0", OccurredAt, CorrelationId);

public record ContaLiberadaEvento(
    string EventId,
    string CorrelationId,
    ContaId ContaId,
    long SequenciaResolvida,
    int Versao,
    DateTimeOffset OccurredAt)
    : DominioEvento(EventId, "ContaLiberada", "1.0", OccurredAt, CorrelationId);

public record OutboxEnvelopeEvento(
    string EventId,
    string Tipo,
    string SchemaVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string PayloadJson,
    ContaId? ContaId = null)
    : DominioEvento(EventId, Tipo, SchemaVersion, OccurredAt, CorrelationId);
