using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// 1. Obtenção das chaves pelo Azure Key Vault (se KeyVaultUri estiver configurado)
var keyVaultUri = builder.Configuration["KeyVaultUri"];
var managedIdentityClientId = builder.Configuration["ManagedIdentityClientId"];

SecretClient? secretClient = null;

if (!string.IsNullOrEmpty(keyVaultUri))
{
    try
    {
        var credentialOptions = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrEmpty(managedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = managedIdentityClientId;
        }

        var credential = new DefaultAzureCredential(credentialOptions);
        secretClient = new SecretClient(new Uri(keyVaultUri), credential);

        // Busca os segredos no Key Vault e injeta nas ConnectionStrings
        var sqlSecret = secretClient.GetSecret("SqlConnectionString").Value.Value;
        var redisSecret = secretClient.GetSecret("RedisConnectionString").Value.Value;
        var sbSecret = secretClient.GetSecret("ServiceBusConnectionString").Value.Value;

        builder.Configuration["ConnectionStrings:Sql"] = sqlSecret;
        builder.Configuration["ConnectionStrings:Redis"] = redisSecret;
        builder.Configuration["ConnectionStrings:ServiceBus"] = sbSecret;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[AVISO] Falha ao ler Key Vault ({keyVaultUri}): {ex.Message}");
    }
}

// 2. Registro dos Health Checks
var healthChecks = builder.Services.AddHealthChecks();

// Checagem do Key Vault (se cliente foi instanciado)
if (secretClient != null)
{
    healthChecks.AddAsyncCheck("azure_key_vault", async () =>
    {
        try
        {
            // Valida leitura de metadados
            var secret = await secretClient.GetSecretAsync("SqlConnectionString");
            return HealthCheckResult.Healthy("Azure Key Vault acessível e segredos recuperados com sucesso.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Falha ao conectar no Azure Key Vault: {ex.Message}");
        }
    }, tags: new[] { "ready", "security" });
}

// Checagem do Azure SQL Server
var sqlConn = builder.Configuration.GetConnectionString("Sql");
if (!string.IsNullOrEmpty(sqlConn))
{
    healthChecks.AddSqlServer(
        connectionString: sqlConn,
        name: "azure_sql",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "database" });
}

// Checagem do Azure Managed Redis
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    healthChecks.AddRedis(
        redisConnectionString: redisConn,
        name: "azure_redis",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "cache" });
}

// Checagem do Azure Service Bus
var sbConn = builder.Configuration.GetConnectionString("ServiceBus");
if (!string.IsNullOrEmpty(sbConn))
{
    healthChecks.AddAzureServiceBusQueue(
        connectionString: sbConn,
        queueName: "pix-commands",
        name: "azure_service_bus",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "messaging" });
}

var app = builder.Build();

// 3. Endpoint /health consolidado e detalhado
app.MapGet("/health", async (HealthCheckService healthCheckService, IHostEnvironment env) =>
{
    var report = await healthCheckService.CheckHealthAsync();

    var entriesDict = report.Entries.ToDictionary(
        e => e.Key,
        e => new
        {
            status = e.Value.Status.ToString(),
            duration = e.Value.Duration.ToString(),
            description = e.Value.Description ?? (e.Value.Status == HealthStatus.Healthy ? "OK" : e.Value.Exception?.Message),
            data = e.Value.Data
        }
    );

    var response = new
    {
        status = report.Status.ToString(),
        totalDuration = report.TotalDuration.ToString(),
        timestamp = DateTimeOffset.UtcNow,
        environment = env.EnvironmentName,
        version = "1.0.0",
        servicesCheckedCount = report.Entries.Count,
        entries = entriesDict
    };

    var statusCode = report.Status == HealthStatus.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
    return Results.Json(response, statusCode: statusCode);
});

app.Run();

// Tornar a classe pública para permitir que WebApplicationFactory a referencie nos testes
public partial class Program { }
