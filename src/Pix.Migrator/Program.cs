using System.Reflection;
using DbUp;
using DbUp.Engine;
using Microsoft.Data.SqlClient;

namespace Pix.Migrator;

public enum MigrationType
{
    Expand,
    Contract
}

public record ClassifiedMigration(int Version, string Name, MigrationType Type);

public class Program
{
    public static int Main(string[] args)
    {
        // 1. Standalone command: get-range (no database required)
        if (args.Length >= 1 && args[0] == "--get-range")
        {
            var r = CalculateSchemaRange(GetMigrations());
            Console.WriteLine($"{{\"min\": {r.Min}, \"max\": {r.Max}}}");
            return 0;
        }

        Console.WriteLine("=== Pix.Migrator: Azure SQL Schema Runner ===");

        var connectionString = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("[FATAL] Database connection string not provided. Set AZURE_SQL_CONNECTION_STRING.");
            return 1;
        }

        // 2. Query current database schema from SchemaVersions table
        if (args.Length >= 1 && args[0] == "--get-current-schema")
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NOT NULL
                        SELECT TOP 1 CAST(SUBSTRING(ScriptName, 1, 3) AS INT) FROM dbo.SchemaVersions ORDER BY Applied DESC;
                    ELSE
                        SELECT 0;";
                var scalar = cmd.ExecuteScalar();
                int currentVersion = (scalar != null && scalar != DBNull.Value) ? Convert.ToInt32(scalar) : 0;
                Console.WriteLine(currentVersion);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to read current schema version: {ex.Message}");
                return 1;
            }
        }

        // 3. Schema validation / range check mode
        if (args.Length >= 2 && args[0] == "--verify-range")
        {
            if (!int.TryParse(args[1], out var currentSchema))
            {
                Console.Error.WriteLine("[ERROR] Invalid current schema argument for range verification.");
                return 1;
            }

            var range = CalculateSchemaRange(GetMigrations());
            if (currentSchema < range.Min || currentSchema > range.Max)
            {
                Console.Error.WriteLine($"[ROLLBACK_REJECTED] Current schema {currentSchema} outside target range [{range.Min}, {range.Max}].");
                return 2;
            }

            Console.WriteLine($"[VERIFIED] Schema {currentSchema} is within valid range [{range.Min}, {range.Max}].");
            return 0;
        }

        var migrations = GetMigrations();
        if (!ValidateNoContractPairedWithExpand(migrations, out var validationError))
        {
            Console.Error.WriteLine($"[VALIDATION_ERROR] {validationError}");
            return 3;
        }

        EnsureDatabase.For.SqlDatabase(connectionString);

        var timeoutSeconds = GetCommandTimeoutSeconds();
        Console.WriteLine($"[INFO] Configuring DbUp execution command timeout: {timeoutSeconds}s.");

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(Pix.Processing.Infrastructure.DbUpRunner).Assembly, s => s.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .WithTransactionPerScript()
            .WithExecutionTimeout(TimeSpan.FromSeconds(timeoutSeconds))
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.Error.WriteLine($"[MIGRATION_FAILED] {result.Error}");
            return 4;
        }

        var executedRange = CalculateSchemaRange(migrations);
        Console.WriteLine($"[SUCCESS] Database schema successfully migrated to version {executedRange.Max} (SchemaMin: {executedRange.Min}).");
        return 0;
    }

    public static List<ClassifiedMigration> GetMigrations()
    {
        return new List<ClassifiedMigration>
        {
            new(1, "001_InitialSchema", MigrationType.Expand),
            new(2, "002_PixOrdersTable", MigrationType.Expand),
            new(3, "003_OutboxAndLedger", MigrationType.Expand),
            new(4, "004_AccountLocks", MigrationType.Expand),
            new(5, "005_WebhookDeadLetter", MigrationType.Expand),
            new(6, "006_IdempotencyKeys", MigrationType.Expand),
            new(7, "007_AddPartnerReferenceNullable", MigrationType.Expand),
            new(8, "008_SyncReplicaViews", MigrationType.Expand)
        };
    }

    public static (int Min, int Max) CalculateSchemaRange(IEnumerable<ClassifiedMigration> migrations)
    {
        var list = migrations.ToList();
        if (!list.Any()) return (1, 1);

        var max = list.Max(m => m.Version);
        var contractMigrations = list.Where(m => m.Type == MigrationType.Contract).ToList();
        var min = contractMigrations.Any() ? contractMigrations.Min(m => m.Version) : list.Min(m => m.Version);

        return (min, max);
    }

    public static bool ValidateNoContractPairedWithExpand(IEnumerable<ClassifiedMigration> migrations, out string? error)
    {
        var list = migrations.ToList();
        var expands = list.Where(m => m.Type == MigrationType.Expand).Select(m => m.Name.Replace("Expand", "").Replace("00", "")).ToHashSet();
        var contracts = list.Where(m => m.Type == MigrationType.Contract).Select(m => m.Name.Replace("Contract", "").Replace("00", "")).ToHashSet();

        var paired = expands.Intersect(contracts).ToList();
        if (paired.Any())
        {
            error = $"Cannot pair Contract and Expand migration for same feature in single release: {string.Join(", ", paired)}";
            return false;
        }

        error = null;
        return true;
    }

    public static int GetCommandTimeoutSeconds()
    {
        var env = Environment.GetEnvironmentVariable("MIGRATION_COMMAND_TIMEOUT_SECONDS");
        if (int.TryParse(env, out var seconds) && seconds > 0)
            return seconds;
        return 300; // 5 minutes default for long-running DDL operations
    }
}
