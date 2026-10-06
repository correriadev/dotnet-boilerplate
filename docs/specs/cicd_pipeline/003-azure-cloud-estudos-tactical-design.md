# Tactical Design: Solution Space — CI/CD Pipeline & Delivery

Domain: `cicd_pipeline`  
Project: `azure-cloud-estudos`  
Revision: 3 (alinhada aos Gates Manuais de Aprovação e 3 Estágios de Ambiente)  
Reference: [001-problem-space.md](./001-problem-space.md), [002-context-map.md](./002-context-map.md), [pbb_cicd_pipeline.md](../../feature/pbb_cicd_pipeline.md)

All snippets are illustrative pseudocode (max 4 lines).

---

## 1. Aggregates

### 1.1. `ReleaseManifest` (Root)
* **Invariants:**
  * Exatamente 6 digests distintos: 5 hosts (`CashOutApi`, `PaymentApi`, `OutbankConnector`, `ReconciliationWorker`, `WebhookDispatcher`) + `Pix.Migrator`.
  * `releaseVersion` estritamente crescente e monotônica por commit na `main`.
  * `schemaMin <= schemaMax`; `schemaMax` igual à versão de schema produzida pelo migrator do próprio manifesto.
  * Uma vez `Sealed`, nenhum campo pode ser alterado (imutabilidade absoluta).
  * Assinado digitalmente de forma keyless via Cosign associado ao OIDC do GitHub Actions.
  * Não contém versões ou valores de segredo (segredos são resolvidos via Key Vault References versionless).

```text
class ReleaseManifest extends AggregateRoot:
  releaseVersion: ReleaseVersion, commitSha: GitSha, schemaRange: SchemaRange
  digests: Map<ArtifactIdentifier, ImageDigest>, signatureProof: CosignSignatureProof, status: ManifestStatus
  validate: count(digests) == 6 && schemaRange.isValid() && isSignedKeyless()
```

### 1.2. `EnvironmentPromotionGate` (Root)
Governa a progressão sequencial entre ambientes e a concessão de aprovação humana.
* **Invariants:**
  * `Dev` possui deploy contínuo automático após merge na `main`.
  * `Hml` só desbloqueia após `Dev` concluído com sucesso (`DevValidationCompleted` com 100% dos probes e smoke tests aprovados).
  * `Hml` exige aprovação manual obrigatória (`HumanApprovalGrant` emitido por Tech Lead ou QA autorizado).
  * `Prod` só desbloqueia após `Hml` concluído com sucesso (`HmlValidationCompleted` com testes de contrato e integração aprovados).
  * `Prod` exige aprovação manual obrigatória (`HumanApprovalGrant` emitido por Release Manager ou SRE autorizado).
  * Proibido salto direto `Dev -> Prod` sem homologação prévia e aprovada em `Hml`.
  * A promoção para `Hml` e `Prod` utiliza rigorosamente os **mesmos 6 digests SHA-256** do manifesto selado em `main`.

```text
class EnvironmentPromotionGate extends AggregateRoot:
  manifest: ReleaseManifest, devStatus: StageStatus, hmlStatus: StageStatus, prodStatus: StageStatus
  unlockHml(): require devStatus.isPassed(); return GateStatus.WaitingApproval
  authorizeHml(grant: HumanApprovalGrant): require grant.isValid(Env.Hml) && devStatus.isPassed()
  authorizeProd(grant: HumanApprovalGrant): require grant.isValid(Env.Prod) && hmlStatus.isPassed()
```

### 1.3. `EnvironmentReleaseState` (Root)
Estado de release por ambiente: o manifesto ativo, versão de schema e o direito exclusivo de alterar o ambiente.
* **Invariants:**
  * Um único deploy em andamento por ambiente (lock exclusivo de ambiente).
  * `promote(m, grant)` só é aceito se `m.releaseVersion > active.releaseVersion` e pré-requisitos de governança satisfeitos.
  * `rollback(m)` só é aceito se `m.schemaRange.contains(currentSchemaVersion)` e assinatura Cosign for válida.

```text
class EnvironmentReleaseState extends AggregateRoot:
  env: EnvironmentName, active: ReleaseManifest, currentSchema: SchemaVersion, lock: EnvironmentLock
  promote(m, grant): require isAuthorized(m, grant) && m.releaseVersion > active.releaseVersion
  rollback(m): require m.isSealed() && m.schemaRange.contains(currentSchema)
```

### 1.4. `RevisionDeployment` (Root)
* **Invariants:**
  * Nenhuma revisão de aplicação é ativada antes de `DatabaseSchemaMigrated` do mesmo manifesto ser concluído pelo `Pix.Migrator`.
  * APIs HTTP: tráfego só muda para 100% na candidata após `/health/ready` = 200 e bake window de 10 minutos sem anomalias.
  * Workers nunca rodam com múltiplas revisões simultâneas ativas (Single Active Revision).
  * Desligamento de workers respeita o `GracePolicy` do seu arquétipo.

```text
class RevisionDeployment extends AggregateRoot:
  deploymentId: DeploymentId, env: EnvironmentName, manifest: ReleaseManifest
  hostStates: Map<ArtifactIdentifier, HostRevisionState>
  validate: manifest.isSealed() && migrationCompletedFor(manifest, env)
```

### 1.5. `QualityGateVerdict` (Root)
* **Invariants:**
  * Zero testes com falha; zero testes ignorados ou pulados.
  * `executed >= baselineCount` (mínimo de 138 testes).
  * Cobertura global $\ge 85\%$ e por camada: Domain $\ge 90\%$, Application $\ge 85\%$, Infrastructure $\ge 80\%$.
  * `TestEnvironmentFingerprint` com imagens fixadas por digest e motor `SqlServer` (eliminando Postgres).

```text
class QualityGateVerdict extends AggregateRoot:
  failed: int, skipped: int, executed: int, coverage: LayeredCoverage
  environment: TestEnvironmentFingerprint
  validate: failed == 0 && skipped == 0 && environment.isPinnedSqlServer()
```

### 1.6. `SchemaCompatibilityCheck` (Root)
* **Invariants:**
  * Toda migração possui classificação estrita (`Expand` ou `Contract`).
  * Uma migração `Contract` não pode entrar na mesma release do `Expand` correspondente.
  * A suíte de compatibilidade da revisão N-1 contra o schema N precisa passar sem falhas no PR.

```text
class SchemaCompatibilityCheck extends AggregateRoot:
  migrations: List<ClassifiedMigration>, previousRevisionResult: TestOutcome
  validate: noContractPairedWithExpandInSameRelease(migrations)
  derive: SchemaRange from migrations
```

### 1.7. `RollbackOperation` (Root)
* **Invariants:**
  * O manifesto alvo está selado e registrado no histórico.
  * Assinatura Cosign do manifesto e das imagens alvo é verificada antes da execução.
  * A faixa de schema do manifesto alvo contém a versão atual do schema do banco (`schemaMin <= currentSchema <= schemaMax`).
  * O banco de dados nunca sofre rollback destrutivo de schema; apenas binários de aplicação e tráfego são revertidos.
  * Reversão sem rebuild em minutos.

```text
class RollbackOperation extends AggregateRoot:
  env: EnvironmentName, target: ReleaseManifest, reason: string
  validate: target.isSealed() && target.schemaRange.contains(currentSchema) && cosignVerified(target)
```

---

## 2. Value Objects

```text
enum ArtifactIdentifier: CashOutApi, PaymentApi, OutbankConnector, ReconciliationWorker, WebhookDispatcher, Migrator
enum EnvironmentName: Dev, Hml, Prod
enum HostArchetype: HttpEdgeApi, SessionWorker, LeaseWorker, FiniteJob
enum GateStatus: Locked, WaitingApproval, Approved, Rejected, BypassedDisallowed
```

```text
class ReleaseVersion extends ValueObject: value: long; validate: value > 0
class SchemaRange extends ValueObject: min: int, max: int; contains(v): min <= v <= max
class ImageDigest extends ValueObject: hash: string; validate: regex("^[a-f0-9]{64}$")
class CosignSignatureProof extends ValueObject: issuer: string, subject: string, verified: bool
```

```text
class HumanApprovalGrant extends ValueObject:
  env: EnvironmentName, reviewer: string, role: string, approvedAt: DateTime, targetVersion: ReleaseVersion
  validate: (env == Hml && role in ["TechLead", "QA"]) || (env == Prod && role in ["ReleaseManager", "SRE"])
```

```text
class TrafficAllocation extends ValueObject: active: int, candidate: int; validate: active + candidate == 100
class GracePolicy extends ValueObject: seconds: int, basis: LeaseTtl | SessionLockBudget
class SecretReference extends ValueObject: vault: string, name: string  // versionless reference
```

`GracePolicy` por arquétipo:
* `SessionWorker` (`Outbank.Connector`): `seconds > partnerTimeout(2s) + pollyRetryBudget + sessionLockRenewalBudget`.
* `LeaseWorker` (`Pix.Reconciliation.Worker`, `Webhook.Dispatcher`): `seconds > leaseTtl`; para de adquirir leases imediatamente ao receber SIGTERM; completa apenas transações com CAS lease ativo.

---

## 3. Domain Services

### 3.1. `ManifestAssemblyService`
```text
execute(commitSha, digests[6], schemaRange, scanResult): ReleaseManifest
  require scanResult.noUnwaivedCriticalOrHigh()
  version = releaseVersionSequence.next()
  m = ReleaseManifest.seal(version, commitSha, digests, schemaRange)
  proof = cosignPort.signKeyless(m); m.attachProof(proof); return m
```

### 3.2. `PromotionGovernanceService`
```text
evaluateProgression(gate, targetEnv, grantOpt): PromotionDecision
  if targetEnv == Dev: return PromotionDecision.AllowedAutomated
  if targetEnv == Hml:
    require gate.devStatus.isPassed() && grantOpt.isPresented() && grantOpt.isValidFor(Hml)
    return PromotionDecision.AllowedManual(grantOpt)
  if targetEnv == Prod:
    require gate.hmlStatus.isPassed() && grantOpt.isPresented() && grantOpt.isValidFor(Prod)
    return PromotionDecision.AllowedManual(grantOpt)
  throw DisallowedPromotionTransitionException()
```

### 3.3. `PromotionAdmissionService`
```text
admit(env, manifest, grantOpt): Admission
  require governanceService.evaluateProgression(gate, env, grantOpt).isAllowed()
  require cosignPort.verifySignature(manifest) // valida integridade e proveniência criptográfica
  lock = environmentLock.acquire(env)          // serializa deploy no ambiente
  state.promote(manifest, grantOpt)            // rejeita versão <= ativa
  return Admission(lock, manifest, env)
```

### 3.4. `AsymmetricRolloutOrchestrator`
```text
execute(admission, migrationPort, runtimePort):
  migrationPort.runMigratorJob(manifest.digests[Migrator], admission.env)
  runtimePort.deployBlueGreenApis(manifest, admission.env)
  if admission.env == Prod: runtimePort.observeBakeWindow(durationMinutes = 10)
  runtimePort.rotateWorkers(manifest, gracePolicyByArchetype, admission.env)
```

### 3.5. `DeterministicRollbackCoordinator`
```text
execute(env, target, runtimePort):
  require cosignPort.verifySignature(target)   // valida proveniência do alvo histórico
  state.rollback(target)                       // valida faixa de schema contra schema atual
  runtimePort.revertApiTraffic(target, env)
  runtimePort.rotateWorkers(target, gracePolicyByArchetype, env)
  return RollbackOperation.recordSuccess(env, target)
```

### 3.6. `SmokeTokenProvider`
```text
issue(env): AccessToken
  // Client de smoke dedicado, federado via OIDC no Entra ID; escopo restrito às rotas de smoke
  return entraId.acquireTokenByFederation(smokeClientId(env), smokeScope)
```

---

## 4. Domain Events

| Event | Payload | Condition |
|---|---|---|
| `QualityGatePassed` | `RunId, Executed, Coverage, EnvironmentFingerprint` | Pirâmide aprovada no PR em ambiente fixado. |
| `SchemaCompatibilityVerified` | `SchemaRange, PreviousRevisionOutcome` | Classificação expand/contract válida e N-1 × N aprovado. |
| `ReleaseManifestSealed` | `ReleaseVersion, CommitSha, 6x Digest, SchemaRange` | 6 artefatos compilados, escaneados e assinados via Cosign. |
| `DevMigrationExecuted` | `Dev, MigratorDigest, SchemaVersion` | Job do Pix.Migrator concluído no Azure SQL Dev. |
| `DevRevisionDeployed` | `Dev, ManifestVersion, Digests[6]` | Revisões atualizadas no Container Apps Dev. |
| `DevValidationCompleted` | `Dev, ProbesResult, SmokeResult` | Health probes 200 e smoke tests com token de runtime aprovados em Dev. |
| `HomologationGateUnlocked` | `ManifestVersion, DevValidationEvidence` | Desbloqueia gate manual para o Environment `hml`. |
| `HomologationDeploymentApproved`| `ManifestVersion, Reviewer, ApprovedAt` | Ação humana formal aprovada no GitHub Actions para `hml`. |
| `HmlMigrationExecuted` | `Hml, MigratorDigest, SchemaVersion` | Job do Pix.Migrator concluído no Azure SQL Hml (mesmo digest). |
| `HmlRevisionDeployed` | `Hml, ManifestVersion, Digests[6]` | Revisões atualizadas em Hml (mesmos digests). |
| `HmlValidationCompleted` | `Hml, ContractTestsOutcome, IntegrationOutcome`| Testes de contrato com parceiros e integração aprovados em Hml. |
| `ProductionGateUnlocked` | `ManifestVersion, HmlValidationEvidence` | Desbloqueia gate manual para o Environment `prod`. |
| `ProductionDeploymentApproved` | `ManifestVersion, Reviewer, ApprovedAt` | Ação humana formal aprovada no GitHub Actions para `prod`. |
| `ProdMigrationExecuted` | `Prod, MigratorDigest, SchemaVersion` | Job do Pix.Migrator concluído no Azure SQL Prod (mesmo digest). |
| `ProdBlueGreenTrafficShifted` | `Prod, ManifestVersion, TrafficAllocation` | APIs com probes 200 e bake window de 10 min OK; tráfego em 100%. |
| `ProdWorkersGracefullyRotated` | `Prod, ManifestVersion, GracePolicyByHost` | Workers rotacionados com respeito ao lease TTL e lock do Service Bus. |
| `ProductionHealthConfirmed` | `Prod, ManifestVersion, ObservabilityVerdict` | Produção em regime saudável e sem erros. |
| `ManifestVersionAdmitted` | `Env, ReleaseVersion, ActiveVersion` | Versão monotônica aceita para o ambiente. |
| `PromotionRejectedOutOfOrder` | `Env, ReleaseVersion, ActiveVersion` | Rejeitada promoção de versão <= ativa ou sem gate aprovado. |
| `RollbackExecuted` | `Env, TargetVersion, CurrentSchema` | Reversão sem rebuild realizada com sucesso. |
| `RollbackRejectedIncompatibleSchema` | `Env, TargetVersion, CurrentSchema` | Reversão abortada por schema do banco fora da faixa do alvo. |
| `SecretRotated` | `Vault, SecretName, ValidityWindow` | Rotação por dupla validade sem release de código. |

---

## 5. Ports

```text
interface IContainerRegistryPort: push(artifact, tag); resolveDigest(artifact, tag): ImageDigest; publishManifest(m)
interface ICosignVerificationPort: signKeyless(m): CosignSignatureProof; verifySignature(m): bool
interface IPromotionGovernancePort: checkStageStatus(env): StageStatus; recordApproval(grant: HumanApprovalGrant)
interface IEnvironmentStatePort: acquireLock(env); readActive(env): ReleaseManifest; writeActive(env, m); readSchemaVersion(env)
interface ICloudRuntimePort: deployCandidate(host, digest, env); verifyProbe(rev, path); shiftTraffic(host, alloc, env); rotateWorker(host, digest, grace, env)
interface IDatabaseMigrationPort: runMigratorJob(digest, env): MigrationJobResult
```

---

## Section 6 — Ordered Development Tasks

```json
[
  {
    "id": "T000",
    "title": "Environment parity and cleanup (remove postgres, pin digests, base images, dependabot)"
  },
  {
    "id": "T001",
    "title": "Executable host Pix.Migrator (DbUp console runner, Dockerfile, expand/contract classification)"
  },
  {
    "id": "T002",
    "title": "Workflow ci-pr.yml (pyramid execution, layered coverage, N-1 compatibility, parallel 6 Dockerfiles build)"
  },
  {
    "id": "T003",
    "title": "Workflow package-release.yml (OIDC Entra ID, push ACR, Trivy, SBOM, Cosign, release-manifest.json)"
  },
  {
    "id": "T004",
    "title": "Workflow deploy-dev.yml (automatic deploy on main merge, Cosign verification, ACA Migrator job, Blue/Green APIs, workers GracePolicy, smoke tests)"
  },
  {
    "id": "T005",
    "title": "Workflow promote-hml.yml (manual approval gate, Cosign verify, identical 6 digests, Migrator job, integration/contract partner tests)"
  },
  {
    "id": "T006",
    "title": "Workflow promote-prod.yml (manual approval gate post-HML, Cosign verify, identical 6 digests, Migrator job, Blue/Green 10m bake window, workers GracePolicy)"
  },
  {
    "id": "T007",
    "title": "Workflow rollback.yml (schema range check, Cosign verify, instant traffic/workers revert without rebuild, audit log)"
  }
]
```

### Detalhamento das Tarefas

1. **Task 00 — Paridade de ambiente e limpeza de infraestrutura:**
   * Remover PostgreSQL e variáveis associadas do `docker-compose.yml`;
   * Fixar imagens base de todos os Dockerfiles por digest SHA-256;
   * Fixar SQL Server, Redis e Service Bus Emulator por digest SHA-256 nos testes;
   * Configurar Dependabot com versioning agrupado.
2. **Task 01 — Host Executável `Pix.Migrator`:**
   * Empacotar o `DbUpRunner` como aplicação console executável com Dockerfile próprio;
   * Implementar classificação estrita de migrações (`Expand` / `Contract`);
   * Implementar cálculo de faixa de compatibilidade (`schemaMin`/`schemaMax`).
3. **Task 02 — Workflow `ci-pr.yml`:**
   * Executar pirâmide de testes completa (zero falhas, zero ignorados, contagem $\ge 138$);
   * Aferir barreiras de cobertura por camada (Domain 90%, Application 85%, Infrastructure 80%, Global 85%);
   * Executar suíte de compatibilidade N-1 × schema N quando houver alterações em `[pix]`;
   * Compilar os 6 Dockerfiles em paralelo sem realizar push.
4. **Task 03 — Workflow `package-release.yml`:**
   * Autenticar via OIDC federado com Entra ID;
   * Compilar e publicar os 6 artefatos no ACR;
   * Executar vulnerability scan com Trivy e gerar SBOM com Syft;
   * Assinar keyless imagens e manifesto via Cosign;
   * Selar `release-manifest.json` com versão monotônica e faixa de schema.
5. **Task 04 — Workflow `deploy-dev.yml` (Deploy Contínuo Automático em Dev):**
   * Disparo automático exclusivamente no merge da `main`;
   * Verificar assinatura Cosign;
   * Executar job do `Pix.Migrator` no Azure SQL Dev;
   * Deploy Blue/Green nas APIs e substituição de workers com `GracePolicy`;
   * Executar smoke tests com token de runtime emitido via Entra ID OIDC.
6. **Task 05 — Workflow `promote-hml.yml` (Gate Manual de Homologação):**
   * Configurar GitHub Environment `hml` com Environment Protection Rules (aprovação obrigatória de Tech Lead / QA);
   * Exigir pré-requisito de sucesso em `dev`;
   * Promover estritamente os **mesmos 6 digests SHA-256** do manifesto;
   * Executar job do `Pix.Migrator` no Azure SQL Hml;
   * Executar suíte estendida de integração e testes de contrato contra mocks de parceiros (Outbank/Bacen).
7. **Task 06 — Workflow `promote-prod.yml` (Gate Manual de Produção):**
   * Configurar GitHub Environment `prod` com Environment Protection Rules (aprovação obrigatória de Release Manager / SRE);
   * Exigir pré-requisito de sucesso em `hml` (proibido salto Dev $\to$ Prod);
   * Promover estritamente os **mesmos 6 digests SHA-256** do manifesto;
   * Executar job do `Pix.Migrator` no Azure SQL Prod;
   * Executar Blue/Green com bake window de 10 minutos nas APIs e rotação suave dos workers;
   * Monitorar métricas de observabilidade.
8. **Task 07 — Workflow `rollback.yml` (Reversão Rápida Determinística):**
   * Validar faixa `schemaMin`/`schemaMax` do manifesto anterior contra o schema atual do banco;
   * Verificar assinatura Cosign do manifesto alvo;
   * Reverter tráfego HTTP das APIs e digests dos workers sem recompilação de código;
   * Registrar auditoria de reversão.
