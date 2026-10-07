namespace Pix.Processing.Domain.Telemetry;

public sealed class InspecaoBakePeriod
{
    private readonly List<string> _anomalias = new();

    public string RevisionName { get; }
    public TimeSpan JanelaTotal { get; }
    public DateTimeOffset IniciadoEm { get; }
    public string Status { get; private set; } = "EmObservacao";
    public IReadOnlyList<string> Anomalias => _anomalias.AsReadOnly();

    public InspecaoBakePeriod(string revisionName, TimeSpan janelaTotal, DateTimeOffset iniciadoEm)
    {
        RevisionName = revisionName ?? throw new ArgumentNullException(nameof(revisionName));
        JanelaTotal = janelaTotal;
        IniciadoEm = iniciadoEm;
    }

    public void RegistrarAnomaliaDavis(string problemId, string rca)
    {
        if (Status == "Estavel")
            throw new InvalidOperationException("Inspeção já finalizada como Estável.");

        _anomalias.Add($"Problem: {problemId} | RCA: {rca}");
        Status = "Degradado";
    }

    public void FinalizarInspecao(DateTimeOffset agora)
    {
        if (_anomalias.Any())
        {
            Status = "Degradado";
            return;
        }

        if (agora - IniciadoEm >= JanelaTotal)
        {
            Status = "Estavel";
        }
    }
}
