using Xunit;

namespace Pix.HealthCheck.Migrator.Tests;

public class MigratorRunnerTests
{
    [Fact]
    public async Task RunAsync_WithoutKeyVaultUri_ShouldReturnExitCode1()
    {
        // Arrange
        var envVars = new Dictionary<string, string?>
        {
            ["KeyVaultUri"] = null,
            ["ManagedIdentityClientId"] = null
        };

        // Act
        var exitCode = await MigratorRunner.RunAsync(envVars);

        // Assert
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_WithDirectInvalidConnectionString_ShouldFailGracefullyWithExitCode1()
    {
        // Arrange
        var envVars = new Dictionary<string, string?>
        {
            ["KeyVaultUri"] = null,
            ["SqlConnectionString"] = "Server=tcp:invalid-nonexistent-server.database.windows.net,1433;Database=sqldb-fake;Connection Timeout=2;"
        };

        // Act
        var exitCode = await MigratorRunner.RunAsync(envVars);

        // Assert
        Assert.Equal(1, exitCode);
    }
}

