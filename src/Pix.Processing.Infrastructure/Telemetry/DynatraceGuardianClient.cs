using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pix.Processing.Domain.Telemetry;

namespace Pix.Processing.Infrastructure.Telemetry;

public class DynatraceGuardianClient : IDynatraceGuardianClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;

    public DynatraceGuardianClient(HttpClient httpClient, string apiToken)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiToken = apiToken ?? throw new ArgumentNullException(nameof(apiToken));
    }

    public async Task<string> DispararAvaliacaoAsync(EventoSDLC evento, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/guardians/evaluations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Api-Token", _apiToken);

        var payload = new
        {
            service = "plataforma-pix",
            stage = evento.Environment,
            releaseVersion = evento.ReleaseVersion,
            commitSha = evento.CommitSha,
            manifestDigest = evento.ManifestDigest,
            timestamp = evento.Timestamp.ToUniversalTime().ToString("o")
        };
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (doc.RootElement.TryGetProperty("id", out var idProp))
        {
            return idProp.GetString() ?? Guid.NewGuid().ToString();
        }

        return Guid.NewGuid().ToString();
    }

    public async Task<LaudoGuardian> ConsultarLaudoAsync(string evaluationId, ReleaseMetadata release, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/guardians/evaluations/{Uri.EscapeDataString(evaluationId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Api-Token", _apiToken);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        var violacoes = new List<ViolacaoSLO>();
        if (root.TryGetProperty("violations", out var violationsArray) && violationsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in violationsArray.EnumerateArray())
            {
                var metric = v.TryGetProperty("metric", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                var actual = v.TryGetProperty("actual", out var act) ? act.GetDouble() : 0.0;
                var threshold = v.TryGetProperty("threshold", out var thr) ? thr.GetDouble() : 0.0;
                var message = v.TryGetProperty("message", out var msg) ? msg.GetString() ?? string.Empty : string.Empty;
                violacoes.Add(new ViolacaoSLO(metric, actual, threshold, message));
            }
        }

        var laudo = new LaudoGuardian(evaluationId, release, DateTimeOffset.UtcNow);
        laudo.ConcluirAvaliacao(violacoes);
        return laudo;
    }
}
