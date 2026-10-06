using DbUp;
using DbUp.Engine;
using System.Reflection;

namespace Pix.Processing.Infrastructure;

public static class DbUpRunner
{
    public static UpgradeEngine CriarEngine(string connectionString)
    {
        return DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpRunner).Assembly, s => s.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .LogToNowhere()
            .Build();
    }

    public static DatabaseUpgradeResult Migrar(string connectionString)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        var engine = CriarEngine(connectionString);
        return engine.PerformUpgrade();
    }
}
