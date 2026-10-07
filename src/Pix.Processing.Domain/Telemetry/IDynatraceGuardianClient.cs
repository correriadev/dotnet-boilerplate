namespace Pix.Processing.Domain.Telemetry;

public record EventoSDLC(
    string ReleaseVersion,
    string CommitSha,
    string ManifestDigest,
    string Environment,
    DateTimeOffset Timestamp);

public interface IDynatraceGuardianClient
{
    Task<string> DispararAvaliacaoAsync(EventoSDLC evento, CancellationToken ct = default);
    Task<LaudoGuardian> ConsultarLaudoAsync(string evaluationId, ReleaseMetadata release, CancellationToken ct = default);
}
