namespace Pix.Processing.Domain.Telemetry;

public record ViolacaoSLO(string NomeMetrica, double ValorMedido, double Limiar, string Mensagem);

public class ReleaseMetadata
{
    public string ReleaseVersion { get; }
    public string CommitSha { get; }
    public string ManifestDigest { get; }
    public string Environment { get; }

    public ReleaseMetadata(string releaseVersion, string commitSha, string manifestDigest, string environment)
    {
        ReleaseVersion = releaseVersion ?? throw new ArgumentNullException(nameof(releaseVersion));
        CommitSha = commitSha ?? throw new ArgumentNullException(nameof(commitSha));
        ManifestDigest = manifestDigest ?? throw new ArgumentNullException(nameof(manifestDigest));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }
}

public sealed class LaudoGuardian
{
    private readonly List<ViolacaoSLO> _violacoes = new();

    public string EvaluationId { get; }
    public ReleaseMetadata Release { get; }
    public VereditoQualityGate Veredito { get; private set; }
    public string Status { get; private set; } = "EmAvaliacao";
    public DateTimeOffset Timestamp { get; }
    public IReadOnlyList<ViolacaoSLO> Violacoes => _violacoes.AsReadOnly();

    public LaudoGuardian(string evaluationId, ReleaseMetadata release, DateTimeOffset timestamp)
    {
        EvaluationId = evaluationId ?? throw new ArgumentNullException(nameof(evaluationId));
        Release = release ?? throw new ArgumentNullException(nameof(release));
        Timestamp = timestamp;
        Veredito = VereditoQualityGate.Inconclusive;
    }

    public void ConcluirAvaliacao(IReadOnlyList<ViolacaoSLO> violacoes)
    {
        if (Status == "Concluido")
            throw new InvalidOperationException("Laudo já concluído e imutável.");

        _violacoes.Clear();
        _violacoes.AddRange(violacoes ?? Array.Empty<ViolacaoSLO>());

        bool temViolacaoCritica = _violacoes.Any(v =>
            !string.IsNullOrWhiteSpace(v.NomeMetrica) && (
                v.NomeMetrica.Contains("Latencia", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("Latency", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("response_time", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("Erro", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("failure_rate", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
                v.NomeMetrica.Contains("Bacen", StringComparison.OrdinalIgnoreCase)
            ));

        if (temViolacaoCritica)
        {
            Veredito = VereditoQualityGate.Fail;
        }
        else if (_violacoes.Any())
        {
            Veredito = VereditoQualityGate.Warning;
        }
        else
        {
            Veredito = VereditoQualityGate.Pass;
        }

        Status = "Concluido";
    }
}
