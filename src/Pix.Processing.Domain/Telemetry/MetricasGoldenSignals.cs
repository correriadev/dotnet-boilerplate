namespace Pix.Processing.Domain.Telemetry;

public readonly record struct MetricasGoldenSignals
{
    public double LatenciaP95Ms { get; }
    public double LatenciaP99Ms { get; }
    public double TaxaErroPercent { get; }
    public double VazaoTps { get; }

    public MetricasGoldenSignals(double latenciaP95Ms, double latenciaP99Ms, double taxaErroPercent, double vazaoTps)
    {
        if (double.IsNaN(latenciaP95Ms) || double.IsInfinity(latenciaP95Ms) || latenciaP95Ms < 0)
            throw new ArgumentException("Latência P95 inválida ou negativa.", nameof(latenciaP95Ms));

        if (double.IsNaN(latenciaP99Ms) || double.IsInfinity(latenciaP99Ms) || latenciaP99Ms < 0)
            throw new ArgumentException("Latência P99 inválida ou negativa.", nameof(latenciaP99Ms));

        if (latenciaP99Ms < latenciaP95Ms)
            throw new ArgumentException("Invariante de percentil violada: Latência P99 não pode ser inferior à Latência P95.", nameof(latenciaP99Ms));

        if (double.IsNaN(taxaErroPercent) || double.IsInfinity(taxaErroPercent) || taxaErroPercent < 0.0 || taxaErroPercent > 100.0)
            throw new ArgumentException("Taxa de erro deve ser um valor numérico entre 0.0% e 100.0%.", nameof(taxaErroPercent));

        if (double.IsNaN(vazaoTps) || double.IsInfinity(vazaoTps) || vazaoTps < 0)
            throw new ArgumentException("Vazão TPS inválida ou negativa.", nameof(vazaoTps));

        LatenciaP95Ms = latenciaP95Ms;
        LatenciaP99Ms = latenciaP99Ms;
        TaxaErroPercent = taxaErroPercent;
        VazaoTps = vazaoTps;
    }

    public bool AtendeSlaBacen(double tetoMs = 2000.0) => LatenciaP99Ms <= tetoMs;
}
