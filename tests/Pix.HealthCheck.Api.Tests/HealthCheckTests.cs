using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Pix.HealthCheck.Api.Tests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WithHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.TryGetProperty("status", out var statusProperty));
        Assert.Equal("Healthy", statusProperty.GetString());
        Assert.True(content.TryGetProperty("version", out var versionProperty));
        Assert.Matches(@"^\d+\.\d+\.\d+", versionProperty.GetString());
        Assert.True(content.TryGetProperty("totalDuration", out var durationProperty));
        Assert.False(string.IsNullOrWhiteSpace(durationProperty.GetString()));
        Assert.True(content.TryGetProperty("timestamp", out var timestampProperty));
        Assert.True(content.TryGetProperty("environment", out var envProperty));
        Assert.True(content.TryGetProperty("servicesCheckedCount", out var countProperty));
        Assert.True(content.TryGetProperty("entries", out var entriesProperty));
    }

    [Fact]
    public async Task GetHealth_WhenAnyDependencyUnhealthy_Returns503ServiceUnavailable()
    {
        // Arrange
        var unhealthyFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHealthChecks()
                    .AddAsyncCheck("azure_sql_simulated", () =>
                        Task.FromResult(HealthCheckResult.Unhealthy("Simulated database timeout or pool exhaustion")));
            });
        });

        var client = unhealthyFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.TryGetProperty("status", out var statusProperty));
        Assert.Equal("Unhealthy", statusProperty.GetString());

        Assert.True(content.TryGetProperty("entries", out var entriesProperty));
        Assert.True(entriesProperty.TryGetProperty("azure_sql_simulated", out var entry));
        Assert.Equal("Unhealthy", entry.GetProperty("status").GetString());
        Assert.Equal("Simulated database timeout or pool exhaustion", entry.GetProperty("description").GetString());
    }

    [Fact]
    public async Task GetHealth_WhenDependencyThrowsException_CapturesErrorMessageInReport()
    {
        // Arrange
        var throwingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHealthChecks()
                    .AddAsyncCheck("redis_failing_connection", () =>
                        throw new InvalidOperationException("Redis cluster connection refused"));
            });
        });

        var client = throwingFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.TryGetProperty("entries", out var entriesProperty));
        Assert.True(entriesProperty.TryGetProperty("redis_failing_connection", out var entry));
        Assert.Equal("Unhealthy", entry.GetProperty("status").GetString());
        Assert.Contains("Redis cluster connection refused", entry.GetProperty("description").GetString());
    }

    [Fact]
    public void Program_WithConfiguredConnectionStrings_ExercisesHealthCheckRegistrations()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["KeyVaultUri"] = "https://fake-kv.vault.azure.net/",
            ["ManagedIdentityClientId"] = "00000000-0000-0000-0000-000000000000",
            ["ConnectionStrings:Sql"] = "Server=tcp:localhost,1433;Database=testdb;User Id=sa;Password=fake;",
            ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false",
            ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=fake;SharedAccessKey=fake"
        };

        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(inMemorySettings);
            });
        });

        using var client = customFactory.CreateClient();
        Assert.NotNull(client);
    }
}
