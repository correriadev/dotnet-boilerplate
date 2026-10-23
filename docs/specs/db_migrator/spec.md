# Especificação Técnica de Refinamento de Escopo: Pix.HealthCheck.Migrator

## 1. Contexto do Domínio e Justificativa Arquitetural

* **Problema Identificado:** O Container App Job `job-db-migration-{env}` executava a imagem da API (`Pix.HealthCheck.Api`), subindo o servidor HTTP Kestrel (porta 8080) e mantendo-se em execução contínua até o estouro do timeout de 300 segundos da Azure, classificando a execução como `Failed`.
* **Decisão Arquitetural:** Segregação estrita entre o serviço contínuo (*long-running API daemon*) e a ferramenta de migração de banco (*transient batch CLI*). O migrador de banco de dados passa a ser um projeto e container independente: **`Pix.HealthCheck.Migrator`**.
* **Otimização Ponytail (YAGNI / Menor Código Possível):** Eliminados wrappers customizados de `sp_getapplock` e loggers JSON caseiros. A concorrência já é travada pela esteira (`concurrency: env-traffic-mutation-...`) e pelo Job (`parallelism: 1`), e o DbUp nativo já provê transação única atômica (`.WithTransaction()`) e log de console auditável.

---

## 2. Decisões Consolidadas

| Tópico | Decisão Aprovada | Justificativa Técnica |
|---|---|---|
| **Concorrência / Transação** | Transação única atômica do DbUp (`.WithTransaction()`) | A esteira CI/CD e o Job já garantem execução estritamente serial (`parallelism: 1`). O `.WithTransaction()` do DbUp isola a execução e faz rollback completo de qualquer script que falhar. |
| **Resiliência de Identidade** | `DefaultAzureCredential` com `ManagedIdentityClientId` explícito | Em console app sem DI, a Managed Identity User-Assigned (`uai-pixlab-{env}`) é informada explicitamente nas opções da credencial para recuperação da connection string no Key Vault. |
| **Empacotamento Docker** | Dois Dockerfiles (`Dockerfile` e `Dockerfile.migrator`) | Evita complexidade de condicionais multi-target. `Dockerfile.migrator` usa `runtime:8.0-alpine`, `ENTRYPOINT ["dotnet", "Pix.HealthCheck.Migrator.dll"]`, sem EXPOSE e com `icu-libs` para suporte de globalização. |
| **Coexistência N-1 (Expand & Contract)** | DDL aditivo idempotente | Scripts em `Scripts/` usam `IF NOT EXISTS`. Novas colunas são sempre `NULL` ou possuem `DEFAULT`, garantindo que a versão N-1 da API continue funcional durante a migração. |
| **Telemetria e Logs** | `.LogToConsole()` nativo do DbUp | O DbUp já emite logs linha a linha de cada script executado/ignorado. O Log Analytics da Azure captura automaticamente em `ContainerAppConsoleLogs_CL`. Zero código extra. |
| **Garantia de Término** | Exit Code 0 (Sucesso) e 1 (Falha) | O processo encerra imediatamente após o DbUp, permitindo ao Container App Job detectar o estado `Succeeded` em poucos segundos. |

---

## 3. Estrutura do Novo Componente

```
src/Pix.HealthCheck.Migrator/
├── Pix.HealthCheck.Migrator.csproj
├── Program.cs
├── Scripts/
│   └── 001_initial_schema.sql
```

### 3.1 Implementação Enxuta do `Program.cs` (~35 linhas)

```csharp
using System.Reflection;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using DbUp;

var keyVaultUri = Environment.GetEnvironmentVariable("KeyVaultUri")!;
var miClientId = Environment.GetEnvironmentVariable("ManagedIdentityClientId");

var credentialOptions = new DefaultAzureCredentialOptions();
if (!string.IsNullOrEmpty(miClientId))
{
    credentialOptions.ManagedIdentityClientId = miClientId;
}

var credential = new DefaultAzureCredential(credentialOptions);
var client = new SecretClient(new Uri(keyVaultUri), credential);
var connStr = client.GetSecret("SqlConnectionString").Value.Value;

var upgrader = DeployChanges.To
    .SqlDatabase(connStr)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
    .WithTransaction()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine($"[ERRO] Migração falhou: {result.Error}");
    return 1;
}

Console.WriteLine("[OK] Banco de dados atualizado com sucesso.");
return 0;
```

### 3.2 Script Inicial (`001_initial_schema.sql`)

```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PixTransactions')
BEGIN
    CREATE TABLE PixTransactions (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        EndToEndId NVARCHAR(50) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'PENDING',
        CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
    );

    CREATE NONCLUSTERED INDEX IX_PixTransactions_EndToEndId ON PixTransactions (EndToEndId);
    CREATE NONCLUSTERED INDEX IX_PixTransactions_Status ON PixTransactions (Status);
END
```

---

## 4. Integração CI/CD no GitHub Actions

1. **`package-release.yml`:**
   - Compila a solution (`Pix.HealthCheck.sln`).
   - Constrói `acrlabcorreadev.azurecr.io/pix-healthcheck:<tag>` via `Dockerfile`.
   - Constrói `acrlabcorreadev.azurecr.io/pix-migrator:<tag>` via `Dockerfile.migrator`.
   - Gera SBOM independente com Syft para ambas as imagens.
   - Assina digitalmente ambas as imagens com Cosign.
   - Registra os digests de ambas as imagens no `release-manifest.json` (`services.api` e `services.migrator`).
2. **`deploy-dev.yml` (e HML/PROD):**
   - Extrai o digest imutável de `services.migrator`.
   - Atualiza `job-db-migration-{env}` com a imagem do migrator.
   - Dispara o job de forma síncrona com polling até `status: Succeeded`.
   - Procede para a comutação de tráfego somente com a migração 100% concluída.
