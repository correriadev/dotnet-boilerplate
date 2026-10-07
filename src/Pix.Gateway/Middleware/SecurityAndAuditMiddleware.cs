using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pix.Gateway.Middleware;

public class SecurityAndAuditMiddleware
{
    private static readonly Regex TraversalPattern = new(
        @"(?:^|[/\\])\.\.(?:[/\\]|$)|(?:%2e%2e(?:%2f|%5c|/|\\)|(?:/|\\)%2e%2e)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityAndAuditMiddleware> _logger;

    public SecurityAndAuditMiddleware(RequestDelegate next, ILogger<SecurityAndAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var rawPath = context.Request.Path.Value ?? string.Empty;
        var queryString = context.Request.QueryString.Value ?? string.Empty;

        // TS-INFRA-18: Reject path traversal and URL tampering (with recursive URL-decode and avoiding range false-positives)
        if (IsPathTraversalAttempt(rawPath) || IsPathTraversalAttempt(queryString))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Bad Request: Path traversal or invalid URL sequence detected." });
            return;
        }

        // TS-INFRA-19: Sanitized logging (no Bearer token or passwords exposed)
        var traceId = context.Request.Headers["traceparent"].ToString();
        _logger.LogInformation("Gateway Request: {Method} {Path} [Trace: {TraceId}]", 
            context.Request.Method, 
            SanitizeLog(rawPath), 
            string.IsNullOrWhiteSpace(traceId) ? "N/A" : SanitizeLog(traceId));

        await _next(context);
    }

    public static bool IsPathTraversalAttempt(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // 1. Detectar padrões percent-encoded antes de decodificar
        if (TraversalPattern.IsMatch(input))
            return true;

        // 2. Decodificação recursiva de URL (para tratar encoding duplo e misto)
        var decoded = input;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var next = Uri.UnescapeDataString(decoded);
                if (next == decoded) break;
                decoded = next;
            }
            catch
            {
                return true; // Formato de URL inválido/corrompido é rejeitado
            }
        }

        // 3. Normalização e checagem de directory traversal com delimitadores de caminho
        var normalized = decoded.Replace('\\', '/');
        if (TraversalPattern.IsMatch(normalized) || normalized.Contains("/../") || normalized.StartsWith("../") || normalized.EndsWith("/.."))
            return true;

        return false;
    }

    public static string SanitizeLog(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Redact Bearer tokens
        var sanitized = Regex.Replace(input, @"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
        // Redact password or secret patterns
        sanitized = Regex.Replace(sanitized, @"(password|pwd|secret|token)=[^&\s]+", "$1=[REDACTED]", RegexOptions.IgnoreCase);
        return sanitized;
    }
}
