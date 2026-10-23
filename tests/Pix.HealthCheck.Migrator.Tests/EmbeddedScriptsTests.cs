using System.Reflection;
using Xunit;

namespace Pix.HealthCheck.Migrator.Tests;

public class EmbeddedScriptsTests
{
    [Fact]
    public void Assembly_ShouldContainEmbeddedMigrationScripts()
    {
        // Arrange
        var assembly = typeof(MigratorRunner).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        // Act
        var sqlScripts = resourceNames.Where(r => r.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).ToList();

        // Assert
        Assert.NotEmpty(sqlScripts);
        Assert.Contains(sqlScripts, s => s.Contains("001_initial_schema.sql"));
    }

    [Fact]
    public void InitialSchemaScript_ShouldBeIdempotentWithIfNotExists()
    {
        // Arrange
        var assembly = typeof(MigratorRunner).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith("001_initial_schema.sql", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(resourceName);

        using var stream = assembly.GetManifestResourceStream(resourceName!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var sqlContent = reader.ReadToEnd();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(sqlContent), "Script content must not be empty");
        Assert.Contains("IF NOT EXISTS", sqlContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PixTransactions", sqlContent, StringComparison.OrdinalIgnoreCase);
    }
}

