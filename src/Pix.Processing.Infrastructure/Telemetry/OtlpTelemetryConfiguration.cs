using System.Diagnostics;

namespace Pix.Processing.Infrastructure.Telemetry;

public record OtlpTelemetryOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiToken { get; init; } = string.Empty;
    public string ServiceName { get; init; } = "Pix.Processing";
    public int MaxQueueSize { get; init; } = 2048;
    public int ScheduledDelayMilliseconds { get; init; } = 5000;
    public int ExporterTimeoutMilliseconds { get; init; } = 30000;
    public int MaxExportBatchSize { get; init; } = 512;
}

public static class OtlpTelemetryConfiguration
{
    public static readonly ActivitySource PixActivitySource = new("Pix.Processing", "1.0.0");

    public static OtlpTelemetryOptions ObterOpcoesPadrao(string? endpoint = null, string? apiToken = null, string? serviceName = null)
    {
        return new OtlpTelemetryOptions
        {
            Endpoint = endpoint ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4318",
            ApiToken = apiToken ?? Environment.GetEnvironmentVariable("DYNATRACE_API_TOKEN") ?? string.Empty,
            ServiceName = serviceName ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "Pix.Processing",
            MaxQueueSize = 2048,
            ScheduledDelayMilliseconds = 5000,
            ExporterTimeoutMilliseconds = 30000,
            MaxExportBatchSize = 512
        };
    }
}
