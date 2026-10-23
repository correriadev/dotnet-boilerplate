using System.Reflection;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using DbUp;

namespace Pix.HealthCheck.Migrator;

public static class MigratorRunner
{
    public static async Task<int> RunAsync(IReadOnlyDictionary<string, string?>? env = null)
    {
        string? GetEnv(string key) =>
            env != null && env.TryGetValue(key, out var val)
                ? val
                : Environment.GetEnvironmentVariable(key);

        var keyVaultUri = GetEnv("KeyVaultUri");
        var miClientId = GetEnv("ManagedIdentityClientId");
        var directConnStr = GetEnv("SqlConnectionString");

        string? connStr = directConnStr;

        if (string.IsNullOrWhiteSpace(connStr))
        {
            if (string.IsNullOrWhiteSpace(keyVaultUri))
            {
                Console.Error.WriteLine("[ERRO] Nem 'SqlConnectionString' nem 'KeyVaultUri' foram informados para a migração.");
                return 1;
            }

            try
            {
                Console.WriteLine($"[INFO] Recuperando SqlConnectionString do Azure Key Vault: {keyVaultUri}...");
                var credentialOptions = new DefaultAzureCredentialOptions();
                if (!string.IsNullOrWhiteSpace(miClientId))
                {
                    credentialOptions.ManagedIdentityClientId = miClientId;
                }

                var credential = new DefaultAzureCredential(credentialOptions);
                var client = new SecretClient(new Uri(keyVaultUri), credential);
                var secret = await client.GetSecretAsync("SqlConnectionString");
                connStr = secret.Value.Value;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERRO] Falha ao recuperar SqlConnectionString do Key Vault: {ex.Message}");
                return 1;
            }
        }

        if (string.IsNullOrWhiteSpace(connStr))
        {
            Console.Error.WriteLine("[ERRO] String de conexão SQL está vazia.");
            return 1;
        }

        try
        {
            Console.WriteLine("[INFO] Iniciando motor de migração DbUp (Azure SQL)...");
            var upgrader = DeployChanges.To
                .SqlDatabase(connStr)
                .WithScriptsEmbeddedInAssembly(typeof(MigratorRunner).Assembly)
                .WithTransaction()
                .LogToConsole()
                .Build();

            var result = upgrader.PerformUpgrade();
            if (!result.Successful)
            {
                Console.Error.WriteLine($"[ERRO] Execução das migrações do DbUp falhou: {result.Error}");
                return 1;
            }

            Console.WriteLine("[OK] Migrações de banco de dados executadas com sucesso.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERRO] Exceção não tratada durante migração do banco: {ex.Message}");
            return 1;
        }
    }
}

