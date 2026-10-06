# Strategic Design — Problem Space: CI/CD Pipeline & Delivery

Domain: `cicd_pipeline`  
Project: `azure-cloud-estudos`  
Revision: 3 (alinhada aos Gates Manuais de Aprovação e 3 Estágios de Ambiente)  
Reference: [pbb_cicd_pipeline.md](../../feature/pbb_cicd_pipeline.md), [CICD.md](../../adr/CICD.md), [qa_pipeline.md](../../feature/qa_pipeline.md)

---

## 1. Event Storming

Fluxo temporal completo de eventos do ciclo de integração contínua, empacotamento imutável, deploy automático em Desenvolvimento e promoção governada por gates manuais para Homologação e Produção:

| # | Domain Event (past tense) | Command (trigger) | Aggregate | External Systems | Read Models |
|---|---|---|---|---|---|
| E01 | PullRequestOpened | Open/Update PR | PullRequestLifecycle | GitHub Actions | PullRequestDashboard |
| E02 | CiEnvironmentProvisioned | ProvisionPinnedTestEnvironmentCommand | TestEnvironment | Docker Engine (SQL Server/Redis/SB por digest) | TestEnvironmentFingerprint |
| E03 | QualityGateValidated | RunTestPyramidCommand | QualityGateVerdict | Docker Engine | TestResultsSummary (TRX) |
| E04 | CodeCoverageEnforced | EvaluateCoverageCommand | QualityGateVerdict | Coverlet | CodeCoverageReport |
| E05 | SchemaCompatibilityVerified | RunPreviousRevisionAgainstNewSchemaCommand | SchemaCompatibilityCheck | Docker Engine (SQL Server) | SchemaCompatibilityReport |
| E06 | DockerfilesValidated | TestContainerBuildCommand | ContainerPackaging | Docker Engine | BuildStatusReport |
| E07 | CodeMergedToMain | MergePullRequestCommand | BranchGovernance | GitHub VCS | GitCommitTimeline |
| E08 | AzureOidcAuthenticated | AuthenticateCloudSessionCommand | FederatedIdentitySession | Microsoft Entra ID | CloudSecurityContext |
| E09 | ContainerImagesBuilt | BuildAndTagImagesCommand | ContainerPackaging | Docker Buildx / ACR | ImageBuildRegistry |
| E10 | VulnerabilityScanCompleted | ScanContainerImagesCommand | SecurityCompliance | Trivy | SarifVulnerabilityReport |
| E11 | SbomGenerated | GenerateSbomCommand | SecurityCompliance | Syft / CycloneDX | SbomCatalog |
| E12 | ReleaseManifestSealedAndSigned | SealAndSignManifestCommand | ReleaseManifest | ACR (OCI Artifact) / Cosign | ReleaseCatalogView |
| E13 | DevMigrationExecuted | ExecuteMigratorJobCommand | DatabaseLifecycle | ACA Job (digest do migrator) / SQL Dev | MigrationLedger (Dev) |
| E14 | DevRevisionDeployed | DeployDevRevisionCommand | RevisionDeployment | Azure Container Apps (Dev) | DevTopologyStatus |
| E15 | DevValidationCompleted | RunDevSmokeTestsCommand | RuntimeHealthVerification | Container Probes / Smoke Client | DevHealthCheckBoard |
| E16 | HomologationGateUnlocked | UnlockHomologationGateCommand | EnvironmentPromotionGate | GitHub Actions Environment Engine | EnvironmentApprovalQueue |
| E17 | HomologationDeploymentApproved | ApprovePromotionCommand (Human Action) | EnvironmentPromotionGate | GitHub Environment Reviewer | DeploymentAuditLedger |
| E18 | HmlMigrationExecuted | ExecuteMigratorJobCommand | DatabaseLifecycle | ACA Job (mesmo digest) / SQL Hml | MigrationLedger (Hml) |
| E19 | HmlRevisionDeployed | DeployHmlRevisionCommand | RevisionDeployment | Azure Container Apps (Hml) | HmlTopologyStatus |
| E20 | HmlValidationCompleted | RunHmlIntegrationAndContractTestsCommand | RuntimeHealthVerification | Partner Mocks / Probes | HmlVerificationReport |
| E21 | ProductionGateUnlocked | UnlockProductionGateCommand | EnvironmentPromotionGate | GitHub Actions Environment Engine | EnvironmentApprovalQueue |
| E22 | ProductionDeploymentApproved | ApprovePromotionCommand (Human Action) | EnvironmentPromotionGate | GitHub Environment Reviewer | DeploymentAuditLedger |
| E23 | ProdMigrationExecuted | ExecuteMigratorJobCommand | DatabaseLifecycle | ACA Job (mesmo digest) / SQL Prod | MigrationLedger (Prod) |
| E24 | ProdBlueGreenTrafficShifted | ShiftApiTrafficCommand | RevisionDeployment | Azure Container Apps (Prod) | IngressTrafficSplitView |
| E25 | ProdWorkersGracefullyRotated | RotateWorkersCommand | RevisionDeployment | Azure Service Bus & Leases CAS | WorkerConsumerStatus |
| E26 | ProductionHealthConfirmed | AuditProductionHealthCommand | RuntimeHealthVerification | Probes / App Insights | ProdOperationalStatus |
| E27 | RollbackRequested | TriggerRollbackCommand | RollbackOperation | GitHub Actions | RollbackExecutionAudit |
| E28 | RollbackRejectedIncompatibleSchema | ValidateSchemaRangeCommand | RollbackOperation | Azure SQL (MigrationLedger) | RollbackExecutionAudit |
| E29 | PreviousRevisionRestored | RestoreStableDigestsCommand | RevisionDeployment | Azure Container Apps | RecoveredServiceState |

---

## 2. Subdomain Classification

| Subdomain | Type | Justification |
|---|---|---|
| **Promotion Governance & Approval** | **Core** | Governa a máquina de estados de promoção em 3 estágios (`dev` $\to$ `hml` $\to$ `prod`), garantindo que apenas aprovações humanas explícitas autorizem a transição de ambientes. |
| **Release & Packaging** | **Core** | Garante a cadeia de custódia dos 6 artefatos imutáveis, assinatura Cosign, versão monotônica e faixa de schema. |
| **Deployment & Rollout** | **Core** | Executa a implantação progressiva por tipo de host (Blue/Green em APIs, grace periods em workers) e reversão determinística sem rebuild. |
| **Quality & Assurance Gate** | **Supporting** | Executa a pirâmide de testes em ambiente hermético e afere barreiras de cobertura no PR. |
| **Database Migration & Compatibility** | **Supporting** | Garante que o migrator imutável execute em jobs isolados antes dos apps e valida compatibilidade N-1 × N. |
| **Federated Identity & Secret Custody** | **Generic** | Federação OIDC com Entra ID segregada por Environment e consumo de segredos via Managed Identity no Key Vault. |
| **Infrastructure Compliance** | **Generic** | Validação sintática e simulação what-if de templates Bicep. |

---

## 3. Ubiquitous Language Glossary

| Term | Definition | Notes |
|---|---|---|
| **Gate de Aprovação Manual** | Barreira de controle obrigatória do GitHub Environment exigindo intervenção humana (revisores autorizados) para liberar a execução do deploy para o próximo estágio. | Proibido qualquer avanço automático para Homologação ou Produção. |
| **Estágio de Desenvolvimento (`dev`)** | Primeiro ambiente em nuvem; único destino que recebe deployment automático imediato após o merge do commit na branch `main`. | Utilizado para smoke tests e validação perimetral inicial. |
| **Estágio de Homologação (`hml`)** | Segundo ambiente em nuvem, idêntico à produção; acessado exclusivamente via aprovação manual pós-Dev para execução de testes de contrato e integração estendida. | Recebe os mesmos 6 digests imutáveis gerados no build. |
| **Estágio de Produção (`prod`)** | Ambiente operacional final; acessado exclusivamente via aprovação manual formal da liderança técnica pós-HML. | Executa sob OIDC estritamente restrito a `prod`. |
| **Artefato Executável** | Toda imagem que executa código na nuvem: os 5 microsserviços da aplicação + o `Pix.Migrator`. | Todos os 6 artefatos obrigatoriamente selados no manifesto. |
| **Release Manifest Imutável** | Contrato canônico contendo os 6 digests SHA-256, versão monotônica, commit SHA e faixa `schemaMin`/`schemaMax`, assinado via Cosign. | Promovido sem qualquer modificação por Dev, HML e PROD. |
| **Assinatura Cosign** | Assinatura digital keyless via OIDC do GitHub Actions aplicada às imagens e ao manifesto. | Verificação mandatória antes de autorizar deploys em HML, PROD e Rollback. |
| **Faixa de Schema Compatível** | Intervalo de versões de banco `[pix]` (`schemaMin` a `schemaMax`) compatível com o release. | Base de bloqueio para rollbacks que causariam quebra de schema. |
| **Blue/Green Rollout** | Estratégia de publicação de APIs HTTP com revisão candidata a 0% de tráfego, avançando para 100% após confirmação dos probes. | Aplicável exclusivamente a hosts com ingress HTTP. |
| **Graceful Drain de Workers** | Desligamento ordenado onde workers de sessão respeitam timeouts de parceiro e workers de lease suspendem aquisições e respeitam o TTL de lease. | Evita reprocessamento indevido ou pedidos órfãos. |
| **Rollback Determinístico** | Reversão imediata de tráfego e digests para o manifesto anterior estável sem recompilar código. | Requer validação prévia de compatibilidade de schema. |
| **Zero-Secret Pipeline** | Modelo de esteira onde nenhum segredo permanente reside no repositório; acesso provido via claims OIDC efêmeras por Environment. | Conexões resolvidas em runtime via Key Vault. |
