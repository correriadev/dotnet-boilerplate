using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Pix.Billing.Application;
using Pix.Billing.Infrastructure;
using Pix.Processing.Application.Features.AcceptRequest;
using Pix.Processing.Application.Ports;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;
using Pix.Processing.Infrastructure.Auth;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Databases & Fallback configuration (INV-21, INV-23)
var sqlConn = builder.Configuration.GetConnectionString("Sql") ?? builder.Configuration.GetConnectionString("SqlServer");
var redisConn = builder.Configuration.GetConnectionString("Redis");

if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrEmpty(sqlConn))
        throw new InvalidOperationException("SQL Server connection string é obrigatória fora do ambiente de desenvolvimento.");
}

if (!string.IsNullOrEmpty(sqlConn))
{
    builder.Services.AddDbContext<PixDbContext>(opt => opt.UseSqlServer(sqlConn));
    builder.Services.AddDbContext<BillingDbContext>(opt => opt.UseSqlServer(sqlConn));
    builder.Services.AddScoped<System.Data.IDbConnection>(_ => new Microsoft.Data.SqlClient.SqlConnection(sqlConn));
}
else
{
    builder.Services.AddDbContext<PixDbContext>(opt => opt.UseInMemoryDatabase("PixProcessingDb"));
    builder.Services.AddDbContext<BillingDbContext>(opt => opt.UseInMemoryDatabase("BillingDb"));
}

builder.Services.AddScoped<IPedidoPixRepository, Pix.Processing.Infrastructure.Repositories.PedidoPixRepository>();
builder.Services.AddScoped<IOutboxRepository, Pix.Processing.Infrastructure.Repositories.OutboxRepository>();
builder.Services.AddScoped<IHistoricoExecucaoRepository, Pix.Processing.Infrastructure.Repositories.HistoricoExecucaoRepository>();

IConnectionMultiplexer? redisClient = null;
if (!string.IsNullOrEmpty(redisConn))
{
    try
    {
        redisClient = ConnectionMultiplexer.Connect(redisConn);
        builder.Services.AddSingleton(redisClient);
    }
    catch
    {
        // Conexão Redis em modo degradado
    }
}

// Processing Ports & UseCases
builder.Services.AddScoped<IAcceptanceStore, AcceptanceStore>();
builder.Services.AddScoped<IExecutionStore, ExecutionStore>();
builder.Services.AddSingleton<IAdmissionLimiter>(sp => new RedisAdmissionLimiter(5, TimeSpan.FromSeconds(10), redisClient));
builder.Services.AddSingleton<IFinancialCache>(sp => new RedisFinancialCache(redisClient));
builder.Services.AddScoped<IAutorizacaoPort, AutorizacaoAdapter>();

// Authentication
var oidcOptions = builder.Configuration.GetSection("Oidc").Get<OidcAuthOptions>() ?? new OidcAuthOptions
{
    SimulatedMode = builder.Environment.IsDevelopment()
};
builder.Services.AddSingleton(oidcOptions);
builder.Services.AddSingleton<OidcAuthValidator>();
builder.Services.AddSingleton<IOidcAuthValidator>(sp => sp.GetRequiredService<OidcAuthValidator>());

builder.Services.AddScoped<AcceptRequestUseCase>();

// Billing Ports & UseCases
builder.Services.AddScoped<ICobrancaJobStore, CobrancaJobStore>();
builder.Services.AddScoped<RequestBillingUseCase>();

var app = builder.Build();

// OpenTelemetry W3C Trace Context Middleware (INV-20)
app.Use(async (context, next) =>
{
    var activity = Activity.Current ?? new Activity("PixPaymentRequest").Start();

    if (context.Request.Headers.TryGetValue("traceparent", out var traceparent))
    {
        activity.SetParentId(traceparent.ToString());
    }

    if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
    {
        var corr = correlationId.ToString();
        if (!Pix.Processing.Domain.Telemetry.SanitizadorTelemetriaPolicy.ValorContemPii(corr))
            activity.SetTag("correlation_id", corr);
    }

    context.Response.Headers["traceparent"] = activity.Id ?? string.Empty;

    try
    {
        await next();
    }
    finally
    {
        var tagsParaRemover = activity.TagObjects
            .Where(t => Pix.Processing.Domain.Telemetry.SanitizadorTelemetriaPolicy.ContemDadosSensiveis(t.Key) ||
                        Pix.Processing.Domain.Telemetry.SanitizadorTelemetriaPolicy.ObjetoContemPii(t.Value))
            .Select(t => t.Key)
            .Distinct()
            .ToList();

        foreach (var key in tagsParaRemover)
        {
            activity.SetTag(key, null);
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health Probes (INV-21)
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

app.MapGet("/health/ready", async (PixDbContext db, BillingDbContext bDb) =>
{
    bool dbOk = false;
    try
    {
        var pOk = await db.Database.CanConnectAsync();
        var bOk = await bDb.Database.CanConnectAsync();
        dbOk = pOk && bOk;
    }
    catch
    {
        dbOk = false;
    }

    if (!dbOk)
    {
        return Results.Json(new { status = "Unhealthy", error = "Database unavailable" }, statusCode: 503);
    }

    bool redisOk = redisClient != null && redisClient.IsConnected;
    if (!redisOk)
    {
        return Results.Ok(new { status = "Ready", admission = "Degraded" });
    }

    return Results.Ok(new { status = "Ready", admission = "Healthy" });
});

app.MapControllers();

app.Run();

public partial class Program { }
