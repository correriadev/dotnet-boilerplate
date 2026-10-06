namespace Pix.Processing.Domain.Entities;

public class MensagemOutbox
{
    public string EventId { get; init; } = string.Empty;
    public string Tipo { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = "1.0";
    public string CorrelationId { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CriadoEm { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public string? LockOwner { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
