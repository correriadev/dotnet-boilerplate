namespace Pix.Processing.Infrastructure.Entities;

public class IdempotencyRecord
{
    public string ChaveComposta { get; set; } = string.Empty;
    public string ClienteId { get; set; } = string.Empty;
    public string TipoOperacao { get; set; } = string.Empty;
    public string Chave { get; set; } = string.Empty;
    public string FingerprintHash { get; set; } = string.Empty;
    public string FingerprintVersao { get; set; } = string.Empty;
    public Guid PedidoId { get; set; }
    public int StatusCode { get; set; }
    public string HeadersJson { get; set; } = "{}";
    public string CorpoJson { get; set; } = "{}";
    public DateTimeOffset CriadoEm { get; set; }
}

public class OutboxRecord
{
    public string EventId { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "1.0";
    public string CorrelationId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "PENDING";
    public string? Owner { get; set; }
    public long Generation { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
}

public class InboxRecord
{
    public string EventId { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}

public class PendingCommandRecord
{
    public string EventId { get; set; } = string.Empty;
    public Guid PedidoId { get; set; }
    public string ContaId { get; set; } = string.Empty;
    public long Sequencia { get; set; }
    public string CorpoJson { get; set; } = "{}";
    public string Status { get; set; } = "PENDING";
    public int Tentativas { get; set; }
    public DateTimeOffset ProximaTentativa { get; set; }
}
