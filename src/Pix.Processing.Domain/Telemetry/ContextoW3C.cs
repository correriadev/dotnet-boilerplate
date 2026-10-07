using System.Text.RegularExpressions;

namespace Pix.Processing.Domain.Telemetry;

public readonly record struct ContextoW3C
{
    private static readonly Regex TraceParentRegex = new(
        @"^00-[0-9a-fA-F]{32}-[0-9a-fA-F]{16}-[0-9a-fA-F]{2}$",
        RegexOptions.Compiled);

    public string TraceParent { get; }
    public string? TraceState { get; }
    public string TraceId => !string.IsNullOrEmpty(TraceParent) && TraceParent.Length >= 35 ? TraceParent.Substring(3, 32) : string.Empty;
    public string SpanId => !string.IsNullOrEmpty(TraceParent) && TraceParent.Length >= 52 ? TraceParent.Substring(36, 16) : string.Empty;

    private ContextoW3C(string traceParent, string? traceState)
    {
        TraceParent = traceParent;
        TraceState = traceState;
    }

    public static ContextoW3C Create(string traceParent, string? traceState = null)
    {
        if (string.IsNullOrWhiteSpace(traceParent) || traceParent.Length != 55 || !TraceParentRegex.IsMatch(traceParent))
        {
            throw new ArgumentException("TraceParent W3C inválido. Deve possuir 55 caracteres no formato 00-{traceId}-{spanId}-{flags}.", nameof(traceParent));
        }

        var traceId = traceParent.Substring(3, 32);
        var spanId = traceParent.Substring(36, 16);

        if (traceId == "00000000000000000000000000000000")
        {
            throw new ArgumentException("TraceId W3C inválido: não pode ser composto exclusivamente por zeros (W3C section 3.2.2.3).", nameof(traceParent));
        }

        if (spanId == "0000000000000000")
        {
            throw new ArgumentException("SpanId W3C inválido: não pode ser composto exclusivamente por zeros (W3C section 3.2.2.3).", nameof(traceParent));
        }

        return new ContextoW3C(traceParent, traceState);
    }
}
