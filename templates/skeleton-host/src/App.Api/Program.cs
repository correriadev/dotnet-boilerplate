using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configuração de telemetria W3C e OpenTelemetry (Dynatrace OTLP ready)
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: builder.Configuration.GetValue<string>("ServiceName") ?? "App.Api",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddOtlpExporter();
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Regex canônico do formato W3C traceparent (55 caracteres: 00-{32hex}-{16hex}-{2hex})
var w3cRegex = new Regex(@"^00-[0-9a-fA-F]{32}-[0-9a-fA-F]{16}-[0-9a-fA-F]{2}$", RegexOptions.Compiled);

// Endpoint Canônico de Healthcheck com validação e propagação W3C
app.MapGet("/health", (IHostEnvironment env, HttpContext context) =>
{
    var rawHeader = context.Request.Headers["traceparent"].FirstOrDefault();
    var isValid = !string.IsNullOrWhiteSpace(rawHeader) && w3cRegex.IsMatch(rawHeader);

    // Se válido, propaga; caso contrário, usa a atividade atual ou gera fallback W3C válido
    var activeTrace = isValid ? rawHeader! : (Activity.Current?.Id ?? "00-00000000000000000000000000000000-0000000000000000-00");

    return Results.Ok(new
    {
        status = "Healthy",
        timestamp = DateTimeOffset.UtcNow,
        environment = env.EnvironmentName,
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
        traceContext = activeTrace,
        traceContextPropagated = isValid
    });
})
.WithName("HealthCheck")
.WithOpenApi();

app.Run();

// Tornar a classe Program acessível para WebApplicationFactory nos testes
public partial class Program { }
