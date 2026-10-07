using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Pix.Gateway.Transforms;

public class W3CHeaderTransformProvider
{
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";

    private static readonly Regex W3CTraceParentRegex = new(
        @"^00-([0-9a-f]{32})-([0-9a-f]{16})-([0-9a-f]{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidW3CTraceParent(string? traceParent)
    {
        if (string.IsNullOrWhiteSpace(traceParent) || traceParent.Length != 55)
            return false;

        var match = W3CTraceParentRegex.Match(traceParent.ToLowerInvariant());
        if (!match.Success)
            return false;

        var traceId = match.Groups[1].Value;
        var spanId = match.Groups[2].Value;

        // Rejeita all-zero traceId e all-zero spanId conforme RFC W3C Trace Context
        if (traceId == "00000000000000000000000000000000" || spanId == "0000000000000000")
            return false;

        return true;
    }

    public static string ResolveOrGenerateTraceParent(string? incomingTraceParent)
    {
        if (IsValidW3CTraceParent(incomingTraceParent))
            return incomingTraceParent!.ToLowerInvariant();

        // Se há uma Activity ativa no pipeline HTTP compatível com W3C, prioriza sua correlação
        if (Activity.Current != null && IsValidW3CTraceParent(Activity.Current.Id))
            return Activity.Current.Id!.ToLowerInvariant();

        return GenerateW3CTraceParent();
    }

    public static string GenerateW3CTraceParent()
    {
        Span<byte> traceIdBytes = stackalloc byte[16];
        Span<byte> spanIdBytes = stackalloc byte[8];

        RandomNumberGenerator.Fill(traceIdBytes);
        RandomNumberGenerator.Fill(spanIdBytes);

        var traceId = Convert.ToHexString(traceIdBytes).ToLowerInvariant();
        var spanId = Convert.ToHexString(spanIdBytes).ToLowerInvariant();

        return $"00-{traceId}-{spanId}-01";
    }

    public void ApplyW3CTransform(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            var httpContext = transformContext.HttpContext;
            var incomingTraceParent = httpContext.Request.Headers[TraceParentHeader].ToString();

            var outgoingTraceParent = ResolveOrGenerateTraceParent(incomingTraceParent);

            transformContext.ProxyRequest.Headers.Remove(TraceParentHeader);
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(TraceParentHeader, outgoingTraceParent);

            var incomingTraceState = httpContext.Request.Headers[TraceStateHeader].ToString();
            if (!string.IsNullOrWhiteSpace(incomingTraceState))
            {
                transformContext.ProxyRequest.Headers.Remove(TraceStateHeader);
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation(TraceStateHeader, incomingTraceState);
            }

            return ValueTask.CompletedTask;
        });
    }

    public void TransformHeaders(HttpContext context, HttpRequestMessage targetRequest)
    {
        var incomingTraceParent = context.Request.Headers[TraceParentHeader].ToString();
        var outgoingTraceParent = ResolveOrGenerateTraceParent(incomingTraceParent);

        targetRequest.Headers.Remove(TraceParentHeader);
        targetRequest.Headers.TryAddWithoutValidation(TraceParentHeader, outgoingTraceParent);

        var incomingTraceState = context.Request.Headers[TraceStateHeader].ToString();
        if (!string.IsNullOrWhiteSpace(incomingTraceState))
        {
            targetRequest.Headers.Remove(TraceStateHeader);
            targetRequest.Headers.TryAddWithoutValidation(TraceStateHeader, incomingTraceState);
        }
    }
}
