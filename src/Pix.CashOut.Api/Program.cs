using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
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

// Persistence & Fallback configuration (INV-21, INV-23)
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
    builder.Services.AddScoped<System.Data.IDbConnection>(_ => new Microsoft.Data.SqlClient.SqlConnection(sqlConn));
}
else
{
    builder.Services.AddDbContext<PixDbContext>(opt => opt.UseInMemoryDatabase("PixProcessingDb"));
}

builder.Services.AddScoped<IPedidoPixRepository, Pix.Processing.Infrastructure.Repositories.PedidoPixRepository>();
builder.Services.AddScoped<IOutboxRepository, Pix.Processing.Infrastructure.Repositories.OutboxRepository>();
builder.Services.AddScoped<IHistoricoExecucaoRepository, Pix.Processing.Infrastructure.Repositories.HistoricoExecucaoRepository>();

// Redis Connection & Adapters
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
        // Falha de conexão inicial com Redis registrada para modo degradado
    }
}

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

// Use Cases
builder.Services.AddScoped<AcceptRequestUseCase>();

var app = builder.Build();

// OpenTelemetry W3C Trace Context Middleware (INV-20)
app.Use(async (context, next) =>
{
    var activity = Activity.Current ?? new Activity("PixCashOutRequest").Start();

    // W3C Trace Context propagation: traceparent e tracestate
    if (context.Request.Headers.TryGetValue("traceparent", out var traceparent))
    {
        activity.SetParentId(traceparent.ToString());
    }

    if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
    {
        activity.SetTag("correlation_id", correlationId.ToString());
    }

    if (context.Request.Headers.TryGetValue("X-Request-Code", out var requestCode))
    {
        activity.SetTag("request_code", requestCode.ToString());
    }

    // Garante que dados sensíveis/segredos nunca sejam propagados em tags (INV-20)
    context.Response.Headers["traceparent"] = activity.Id ?? string.Empty;

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Probes de Saúde (INV-21)
// Liveness indica apenas a saúde do processo em execução
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

// Readiness preserva a rota de replay se banco/identidade estiverem disponíveis
app.MapGet("/health/ready", async (PixDbContext db) =>
{
    bool dbOk = false;
    try
    {
        dbOk = await db.Database.CanConnectAsync();
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
        // INV-21: Redis indisponível sinaliza admissão degradada, preservando rota de replay acessível
        return Results.Ok(new { status = "Ready", admission = "Degraded" });
    }

    return Results.Ok(new { status = "Ready", admission = "Healthy" });
});

app.MapControllers();

app.Run();

public partial class Program { }
