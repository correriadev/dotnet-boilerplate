var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", (IHostEnvironment env) => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow,
    environment = env.EnvironmentName,
    version = "1.0.0"
}));

app.Run();

// Tornar a classe pública para permitir que WebApplicationFactory a referencie nos testes
public partial class Program { }

