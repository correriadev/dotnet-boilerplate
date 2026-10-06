# Test Scenarios Specification: CI/CD Pipeline & Delivery

Domain: `cicd_pipeline`  
Project: `azure-cloud-estudos`  
Revision: 3 (alinhada aos Gates Manuais de Aprovação e 3 Estágios de Ambiente)  
Reference: [003-azure-cloud-estudos-tactical-design.md](./003-azure-cloud-estudos-tactical-design.md), [pbb_cicd_pipeline.md](../../feature/pbb_cicd_pipeline.md)

Specifications only, Given-When-Then, Ubiquitous Language.

---

## 1. Unit — Aggregates & Value Objects

### `ReleaseManifest`
* **TS-01: Should seal manifest when six valid digests, monotonic release version, valid schema range and Cosign signature are provided**
  * Given digests for the 5 hosts (`CashOutApi`, `PaymentApi`, `OutbankConnector`, `ReconciliationWorker`, `WebhookDispatcher`) and `Pix.Migrator`, `releaseVersion = 42`, `SchemaRange(7, 8)` e assinatura Cosign válida
  * When `ManifestAssemblyService` sela o manifesto
  * Then status é `Sealed`, o manifesto torna-se imutável e `ReleaseManifestSealed` é emitido
* **TS-02: Should reject sealing when the Migrator digest is missing**
  * Given apenas os 5 digests de host (sem o digest do `Pix.Migrator`)
  * When a selagem do manifesto é tentada
  * Then um erro de validação é disparado e nenhum artefato ou manifesto é publicado
* **TS-03: Should reject sealing when schemaMin is greater than schemaMax**
  * Given `SchemaRange(9, 8)`
  * When a selagem é tentada
  * Then um erro de validação é disparado
* **TS-04: Should reject any mutation of a sealed manifest**
  * Given um manifesto com status `Sealed`
  * When qualquer alteração de digest, versão, faixa de schema ou metadados é tentada
  * Then a operação é rejeitada mantendo a imutabilidade absoluta

### `EnvironmentPromotionGate`
* **TS-05: Should allow automated deployment exclusively for Development**
  * Given um manifesto selado e assinado via Cosign
  * When a promoção para o ambiente `dev` é solicitada após merge na `main`
  * Then a promoção é autorizada automaticamente sem exigir aprovação manual
* **TS-06: Should unlock Homologation gate only after Development validation completes successfully**
  * Given que o ambiente `dev` concluiu com 100% dos probes `/health/ready` = 200 e smoke tests aprovados (`DevValidationCompleted`)
  * When o gate de Homologação é avaliado
  * Then o gate transiciona para `WaitingApproval` e `HomologationGateUnlocked` é emitido
* **TS-07: Should reject Homologation deployment when manual approval grant is absent or unauthorized**
  * Given o gate de Homologação em `WaitingApproval`
  * When o deploy em `hml` é acionado sem `HumanApprovalGrant` ou assinado por papel não autorizado
  * Then o deploy é bloqueado e o ambiente `hml` permanece inalterado
* **TS-08: Should reject direct promotion to Production without completed Homologation validation**
  * Given um manifesto aprovado apenas em `dev`, sem passar pela validação de `hml`
  * When uma tentativa de promoção para `prod` é submetida
  * Then a operação é rejeitada com violação de política (`BypassedDisallowed`) e nenhum job é iniciado
* **TS-09: Should unlock Production gate only after Homologation contract and integration tests pass**
  * Given que o ambiente `hml` concluiu com sucesso testes de contrato com parceiros e testes de integração estendida (`HmlValidationCompleted`)
  * When o gate de Produção é avaliado
  * Then o gate transiciona para `WaitingApproval` e `ProductionGateUnlocked` é emitido
* **TS-10: Should reject Production deployment when manual approval grant is absent or unauthorized**
  * Given o gate de Produção em `WaitingApproval`
  * When o deploy em `prod` é acionado sem aprovação formal de Release Manager ou SRE
  * Then a execução é bloqueada e `PromotionRejectedOutOfOrder` é registrado
* **TS-11: Should guarantee that the exact same six digests validated in Development are promoted to HML and PROD**
  * Given os 6 digests SHA-256 do manifesto aprovado em `dev`
  * When as promoções para `hml` e `prod` ocorrem
  * Then os digests baixados e executados em `hml` e `prod` são bit a bit idênticos aos de `dev`, sem recompilação

### `EnvironmentReleaseState`
* **TS-12: Should admit promotion when manifest version is strictly greater than active version**
  * Given versão ativa 41 no ambiente `prod` e `HumanApprovalGrant` válido
  * When a versão 42 é promovida
  * Then `ManifestVersionAdmitted` é emitido e o deploy prossegue
* **TS-13: Should reject promotion when manifest version is lower than or equal to active version**
  * Given versão ativa 42 no ambiente
  * When a versão 41 (ou 42) é submetida
  * Then `PromotionRejectedOutOfOrder` é emitido e o ambiente permanece inalterado
* **TS-14: Should reject rollback when current database schema is outside target manifest schema range**
  * Given schema atual do banco versão 9 e manifesto alvo com `SchemaRange(7, 8)`
  * When o rollback é solicitado
  * Then `RollbackRejectedIncompatibleSchema` é emitido e nenhuma revisão é alterada
* **TS-15: Should accept rollback when current database schema is inside target manifest schema range**
  * Given schema atual do banco versão 8 e manifesto alvo com `SchemaRange(7, 8)`
  * When o rollback é solicitado
  * Then o rollback é admitido para execução

### `QualityGateVerdict`
* **TS-16: Should pass when zero failures, zero skipped tests, executed count at or above baseline and coverage targets are met**
  * Given 0 falhas, 0 ignorados, 138 testes executados, coberturas Domain 91%, Application 86%, Infrastructure 81%, Global 87% contra container SQL Server fixado por digest
  * When o veredito é avaliado
  * Then o resultado é `Passed` e `QualityGatePassed` é emitido
* **TS-17: Should fail when any test is skipped or ignored**
  * Given 0 falhas e 1 teste ignorado
  * When o veredito é avaliado
  * Then o resultado é `Failed` bloqueando o PR
* **TS-18: Should fail when an architectural layer is below its coverage threshold even if global coverage is met**
  * Given Cobertura Global de 88%, mas camada de Infraestrutura em 78% (mínimo 80%)
  * When o veredito é avaliado
  * Then o resultado é `Failed`
* **TS-19: Should fail when test environment uses mutable image tags or non-SQL Server database**
  * Given um `TestEnvironmentFingerprint` referenciando `servicebus-emulator:latest` ou connection string do PostgreSQL
  * When o veredito é avaliado
  * Then o resultado é `Failed` por violação de paridade de ambiente

### `SchemaCompatibilityCheck`
* **TS-20: Should reject a release containing a Contract migration paired with its Expand migration**
  * Given migração 008 (Expand: adicionar coluna nula) e migração 009 (Contract: remover coluna antiga) no mesmo PR
  * When a validação de compatibilidade executa
  * Then a checagem falha com erro de violação do ciclo expand/contract
* **TS-21: Should fail when previous application revision test suite fails against new schema**
  * Given a suíte de testes da revisão N-1 executada contra o schema N resultante com 1 falha
  * When a checagem executa
  * Then a compatibilidade reversa é reprovada e o PR é bloqueado

### Value Objects
* **TS-22: Should accept TrafficAllocation summing to 100 and reject otherwise**
  * Given `(active: 100, candidate: 0)` ou `(active: 0, candidate: 100)` $\to$ aceito; `(active: 50, candidate: 40)` $\to$ rejeitado com erro de validação
* **TS-23: Should reject GracePolicy for LeaseWorker shorter than or equal to lease TTL**
  * Given TTL de lease de 60s e política configurada para 30s
  * When a política é instanciada
  * Then um erro de validação é disparado
* **TS-24: Should reject GracePolicy for SessionWorker shorter than partner timeout plus retry budget plus lock renewal**
  * Given timeout de parceiro de 2s, retry Polly de 3s e renovação de lock de 5s (total 10s), e política configurada para 8s
  * When a política é instanciada
  * Then um erro de validação é disparado
* **TS-25: Should reject CosignSignatureProof when issuer or subject is invalid**
  * Given assinatura Cosign emitida por repositório ou issuer não corporativo
  * When a prova é validada
  * Then a verificação criptográfica falha e a admissão é recusada

---

## 2. Integration — Domain Services

* **TS-26: Should execute Migrator ACA Job using manifest digest before activating any application revision**
  * Given um manifesto selado admitido para o ambiente alvo
  * When `AsymmetricRolloutOrchestrator` executa
  * Then o ACA Job roda `digests[Migrator]` e nenhuma revisão de API ou worker é ativada antes da conclusão de `DatabaseSchemaMigrated`
* **TS-27: Should abort deployment and leave active revisions serving if Migrator job fails**
  * Given que a execução do `Pix.Migrator` falha por timeout ou erro de script
  * When o orchestrator detecta a falha do job
  * Then o deployment é abortado, nenhuma candidata recebe tráfego e as revisões ativas continuam servindo sem interrupção
* **TS-28: Should promote API candidate only after ready probe 200 and bake window completion without anomalies**
  * Given revisão candidata de API com tráfego inicial em 0%
  * When probe `/health/ready` responde HTTP 200 e a janela de bake (10 minutos em Prod) decorre sem erros 5xx
  * Then o tráfego é roteado para 100% na nova revisão
* **TS-29: Should retain 100% traffic on active API revision when candidate ready probe fails**
  * Given revisão candidata de API falhando no probe `/health/ready`
  * When o verificador de probe atua
  * Then a rotação de tráfego é cancelada e 100% das requisições continuam na revisão ativa anterior
* **TS-30: Should stop acquiring leases immediately on shutdown signal and fail CAS completion for reassigned items**
  * Given `Pix.Reconciliation.Worker` com lease próximo ao vencimento durante rotação
  * When o sinal SIGTERM é enviado
  * Then a instância para de disputar novas leases no banco e operações pendentes de itens já reatribuídos falham por otimismo de concorrência sem duplicidade
* **TS-31: Should finish or safely abandon in-flight Service Bus session messages within grace period**
  * Given `Outbank.Connector` processando mensagem com latência de parceiro de 2s e retries
  * When a rotação de revisão é iniciada
  * Then as mensagens em voo são concluídas ou abandonadas com lock liberado dentro da janela de tolerância, sem perda de mensagens
* **TS-32: Should serialize concurrent deployments to the same environment using EnvironmentLock**
  * Given dois disparos de deploy para o mesmo ambiente
  * When ambos tentam obter o lock
  * Then o segundo aguarda ou é rejeitado até a liberação do lock pelo primeiro, garantindo unicidade de execução
* **TS-33: Should restore previous digests and shift traffic without rebuild during rollback**
  * Given um incidente em produção exigindo retorno ao manifesto anterior aprovado
  * When `DeterministicRollbackCoordinator` executa
  * Then os digests anteriores são reativados diretamente no Container Apps e o tráfego HTTP é redirecionado em minutos sem compilar código
* **TS-34: Should acquire smoke test token at runtime via Entra ID OIDC with restricted scope**
  * Given client federado de smoke no Entra ID
  * When `SmokeTokenProvider` emite o token
  * Then nenhuma credencial estática é utilizada, requisições sem token retornam 401 e requisições autenticadas executam com sucesso
* **TS-35: Should ensure zero downtime during Key Vault secret rotation with dual validity**
  * Given um segredo rotacionado no Key Vault com dupla validade ativa
  * When a revisão ativa e uma revisão restaurada em rollback resolvem a credencial via referência versionless
  * Then ambas autenticam perfeitamente sem necessidade de disparar novo release de software

---

## 3. End-to-End Workflows

* **TS-36 (`ci-pr.yml` — Pull Request Quality Gate):**
  * Deve executar a pirâmide completa de testes contra containers reais de SQL Server, Redis e Service Bus Emulator fixados por digest SHA-256;
  * Deve publicar relatório TRX e aferir barreiras de cobertura global ($\ge 85\%$) e por camada (Domain 90%, Application 85%, Infrastructure 80%);
  * Deve rodar a suíte de compatibilidade N-1 contra schema N caso existam migrações no PR;
  * Deve compilar os 6 Dockerfiles em paralelo sem realizar push para registro de containers.
* **TS-37 (`ci-pr.yml` — Isolamento de Fork PR):**
  * Deve executar toda a suíte de testes em PR aberto a partir de fork sem fornecer permissões de escrita, push no ACR ou tokens OIDC no Azure.
* **TS-38 (`package-release.yml` — Empacotamento Imutável e Assinatura):**
  * Deve autenticar via OIDC com Entra ID, compilar os 6 artefatos uma única vez, executar varredura Trivy e SBOM Syft;
  * Deve assinar keyless as imagens e o manifesto com Cosign;
  * Deve registrar o `release-manifest.json` com versão monotônica e faixa `schemaMin`/`schemaMax` no ACR e anexá-lo como GitHub Artifact.
* **TS-39 (`deploy-dev.yml` — Deploy Contínuo em Desenvolvimento):**
  * Deve disparar automaticamente no merge da branch `main`;
  * Deve validar a assinatura Cosign do manifesto;
  * Deve executar o job do `Pix.Migrator` no Azure SQL Dev;
  * Deve implantar as revisões nos Container Apps de Dev, aguardar probes 200 e executar smoke tests via token emitido em runtime.
* **TS-40 (`promote-hml.yml` — Promoção Governada para Homologação):**
  * Deve exigir como pré-requisito a conclusão de `deploy-dev.yml` com sucesso;
  * Deve bloquear a execução do job no GitHub Actions até a aprovação manual de um Tech Lead ou QA no Environment `hml`;
  * Deve validar a assinatura Cosign e promover rigorosamente os **mesmos 6 digests SHA-256** do manifesto;
  * Deve executar o migrator em HML e rodar testes de integração estendida e testes de contrato contra mocks de parceiros.
* **TS-41 (`promote-prod.yml` — Promoção Governada para Produção):**
  * Deve exigir como pré-requisito a aprovação e testes concluídos em `hml` (bloqueio mandatório de saltos diretos Dev $\to$ Prod);
  * Deve bloquear a execução do job no GitHub Actions até a aprovação manual formal de um Release Manager ou SRE no Environment `prod`;
  * Deve validar a assinatura Cosign e promover rigorosamente os **mesmos 6 digests SHA-256** do manifesto;
  * Deve rodar o migrator em Prod, executar Blue/Green com bake window de 10 minutos nas APIs e substituição graciosa dos workers.
* **TS-42 (`promote-prod.yml` — Barreira de Identidade OIDC):**
  * Deve recusar a conexão no Azure caso o token federado OIDC não possua o claim `subject` estritamente mapeado para `repo:<org>/<repo>:environment:prod`.
* **TS-43 (`rollback.yml` — Reversão Determinística):**
  * Deve exigir seleção do manifesto de versão alvo histórica;
  * Deve validar a assinatura Cosign do manifesto alvo;
  * Deve validar que o schema atual do banco está contido na faixa `schemaMin`/`schemaMax` do manifesto alvo;
  * Deve reverter o tráfego HTTP para a revisão anterior e restaurar os digests dos workers sem recompilação, emitindo log formal de auditoria.
