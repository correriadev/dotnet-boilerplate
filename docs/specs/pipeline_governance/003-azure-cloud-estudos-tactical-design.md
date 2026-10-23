# Tactical Design: Governança de Entrega, Nomenclatura CAF e Gate de Retrocompatibilidade

## 0. Refinement Questions and Answers

* **Q01: Como estruturar a validação de retrocompatibilidade N-1 em DEV sem falsos positivos?**
  * **Applies To:** `ALL`
  * **Recommendation:** Capturar a revisão estável $N-1$ antes da migração de banco. Pós-migração, testar a prontidão da candidata $N$ em seu endpoint dedicado (`<app>--rel-X`). Em seguida, submeter a revisão $N-1$ a um teste funcional de leitura e escrita real sobre o schema expandido, e não apenas uma verificação de conectividade genérica (`SELECT 1`).
  * **Answer:** Aprovado. A coexistência exige validação funcional de leitura e escrita por $N-1$.
  * **Answered By:** human

* **Q02: Como definir e tratar o Cold Start (Bootstrap)?**
  * **Applies To:** `azure-cloud-estudos`
  * **Recommendation:** Cold Start é definido estritamente pela inexistência de qualquer revisão prévia ativa no Azure Container Apps (`az containerapp revision list` retorna array vazio). Falha na leitura de segredos no Key Vault ou ambiguidade de estado NÃO é Cold Start; é falha de infraestrutura que deve bloquear a esteira (Fail-Closed).
  * **Answer:** Aprovado. Evita bypass indevido do gate em caso de falha de leitura no Key Vault.
  * **Answered By:** human

* **Q03: Como tratar resolução de FQDN e isolamento da candidata com 0% de tráfego?**
  * **Applies To:** `azure-cloud-estudos`
  * **Recommendation:** Para o tráfego geral, resolver o FQDN dinamicamente via `az containerapp show`. Para testar a candidata $N$ com 0% de tráfego antes da liberação, direcionar requisições especificamente para o FQDN da revisão (`https://<app>--<revision>.<env>.azurecontainerapps.io`) ou label dedicado de revisão.
  * **Answer:** Aprovado.
  * **Answered By:** human

* **Q04: O job de migração deve disparar script de DOWN caso o teste de coexistência N-1 falhe em DEV?**
  * **Applies To:** `ALL`
  * **Recommendation:** Proibido executar `DOWN`. A esteira aborta imediatamente, isola a revisão $N$, mantém $N-1$ em tráfego e aciona incidente para Fix-Forward, preservando a imutabilidade dos dados gerados e a disciplina de Expand-and-Contract.
  * **Answer:** Aprovado.
  * **Answered By:** human

---

## 1. Aggregate Modeling

### 1.1 Aggregate Root: `EnvironmentDeployment`
Controla o ciclo de vida transicional de uma implantação por ambiente.

```csharp
// Shape ilustrativo: Entidade Aggregate Root
public record EnvironmentDeployment(EnvironmentName Env, RevisionId CandidateRev, DeploymentPhase Phase) {
    public bool CanCommitTraffic() => Phase == DeploymentPhase.CompatibilityVerified || Phase == DeploymentPhase.BootstrapAuthorized;
}
```

* **Value Objects:**
  * `EnvironmentName`: Garante valores estritos (`dev`, `hml`, `prod`) e mapeia para os GitHub Environments (`development`, `homologation`, `production`).
  * `RevisionId`: Identificador único de revisão no formato `<containerapp>--rel-<runNumber>`.
  * `CanonicalDeploymentRecord`: Payload JSON atômico contendo runId, commitSha, imageDigest, activeRevision, priorStableRevision, schemaVersion e timestamp UTC.

```csharp
// Shape ilustrativo: Value Object de Nomenclatura CAF
public record ResourceNaming(string BaseApp, EnvironmentName Env) {
    public string ContainerAppName => $"{BaseApp}-{Env.Value}";
    public string MigrationJobName => $"job-db-migration-{Env.Value}";
}
```

* **Máquina de Estados de Implantação (`DeploymentPhase`):**
  1. `Idle`
  2. `LockAcquired`
  3. `StableBaselineCaptured`
  4. `SchemaMigrated`
  5. `CandidateReady`
  6. `CompatibilityVerified` (ou `BootstrapAuthorized`)
  7. `TrafficCommitted`
  8. `Committed`
  9. `ReconciliationRequired` (quando ocorre divergência entre tráfego e ledger)
  10. `FailedClosed`

---

## 2. Domain Services

### 2.1 `BackwardCompatibilityEvaluator`
Avalia a resposta do teste funcional executado pelo binário $N-1$ sobre as tabelas alteradas pela migração $N$.

```csharp
// Shape ilustrativo: Domain Service de Compatibilidade Funcional
public class BackwardCompatibilityEvaluator {
    public CompatibilityVerdict Evaluate(FunctionalTestResult n1Result, HealthReport n1Health) =>
        (n1Result.Success && n1Health.IsHealthy) ? CompatibilityVerdict.Approved : CompatibilityVerdict.Rejected;
}
```

---

## 3. Repositories & State Persistence

### 3.1 `ICanonicalDeploymentLedger`
Persiste e recupera atomicamente o estado canônico do ambiente no Azure Key Vault sob a chave de segredo `DeploymentState`.

```csharp
// Shape ilustrativo: Repositório de Estado Canônico
public interface ICanonicalDeploymentLedger {
    Task RecordCanonicalStateAsync(EnvironmentName env, CanonicalDeploymentRecord record);
    Task<CanonicalDeploymentRecord?> GetCanonicalStateAsync(EnvironmentName env);
}
```

---

## 4. Integration & Events

* **Azure Container Apps:** Manipulação de revisões e ingress via Azure CLI sob identidade OIDC federada restrita por ambiente.
* **Revision-Specific Probing:** Sondagem de endpoints diretos de revisão sem passar pelo Envoy de balanceamento geral.
* **Tratamento de Workers Assíncronos:** O escopo atual cobre a API de Health Check. Em serviços puramente assíncronos (workers com filas do Service Bus), deve-se pausar o consumo de mensagens na revisão candidata até o commit de tráfego.

---

## 5. Error Handling & Resilience

* **Convergência Observada de Tráfego:** Polling ativo de até 60s pós-comutação para validar se requisições sem afinidade atingem a nova revisão antes de declarar sucesso.
* **Fail-Closed em Falha de Coexistência:** Se $N-1$ falhar em operações de banco após o schema ser expandido, a esteira aborta imediatamente, isola $N$ com 0% e aciona incidente, sem assumir que $N-1$ está saudável.
* **Proteção contra Falha de Persistência:** Se a comutação de tráfego ocorrer mas a persistência no Key Vault falhar, a esteira entra no estado `ReconciliationRequired` e bloqueia deploys futuros até reconciliação manual de SRE.

---

## 6. Implementation Tasks (Ordered)

1. **Task 1 — Alignment of Naming & Schema Verifier:**
   * Atualizar `deploy-dev.yml` para `app-pix-worker-dev` e `promote-prod.yml` para `app-pix-worker-prod`.
   * Corrigir `verify-schema-compatibility.ps1` para rejeitar imediatamente ambientes desconhecidos (eliminar fallback para DEV).
   * Corrigir parâmetro `-ContainerAppName` em `rollback.yml` e alinhar mapeamento de nomes.
2. **Task 2 — Dynamic FQDN & Revision-Specific Endpoint:**
   * Inserir resolução de FQDN via `az containerapp show` especificando subscription e resource group.
   * Direcionar teste de prontidão da candidata $N$ para o FQDN dedicado da revisão (`https://<app>--rel-X.../health`).
3. **Task 3 — Backward Compatibility Functional Test in DEV:**
   * Capturar e atestar a revisão $N-1$ estável ANTES de provisionar $N$ e antes da migração de banco.
   * Executar a migração de banco aditiva (Up).
   * Submeter a revisão $N-1$ a teste de leitura/escrita contra o schema expandido.
   * Se aprovado, comutar tráfego para a revisão $N$ com validação de convergência.
4. **Task 4 — Canonical JSON State Ledger in Key Vault:**
   * Consolidar os segredos dispersos em um único payload JSON canônico (`DeploymentState`) com status `COMMITTED`, SHA, digests e revisões ativas/anteriores.
