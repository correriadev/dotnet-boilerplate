# Strategic Design — Context Map: CI/CD Pipeline & Delivery

Domain: `cicd_pipeline`  
Project: `azure-cloud-estudos`  
Revision: 3 (alinhada aos Gates Manuais de Aprovação e 3 Estágios de Ambiente)  
Reference: [001-problem-space.md](./001-problem-space.md), [pbb_cicd_pipeline.md](../../feature/pbb_cicd_pipeline.md)

---

## 1. Bounded Context Identification

| Bounded Context | Responsibility | Boundary (excluded) | Team Ownership | Key Entities |
|---|---|---|---|---|
| **QualityAssuranceContext** | Executa a pirâmide de testes no PR em ambiente fixado por digest, com SQL Server como motor de banco. | Não publica imagens no ACR nem acessa a nuvem Azure. | Engenharia / QA | `TestEnvironment`, `QualityGateVerdict` |
| **SchemaCompatibilityContext** | Classifica migrações em expand/contract, calcula a faixa de schema e prova a compatibilidade N-1 × schema N no PR. | Não executa migrações contra bancos de nuvem. | DBA / Engenharia | `SchemaCompatibilityCheck`, `MigrationClassification` |
| **ReleasePackagingContext** | Compila os 6 artefatos executáveis, realiza scans Trivy, gera SBOM e sela o manifesto com versão monotônica, faixa de schema e assinatura Cosign. | Não realiza deploys em nenhum ambiente. | DevOps / Release | `ReleaseManifest`, `ImageDigest`, `CosignSignatureProof` |
| **PromotionGovernanceContext** | Governa a progressão sequencial entre os 3 ambientes (`dev` $\to$ `hml` $\to$ `prod`), controlando os gates de aprovação manual obrigatória por revisores autorizados. | Não executa comandos diretos de container nem altera código. | Tech Lead / Release Manager | `EnvironmentPromotionGate`, `HumanApprovalGrant`, `EnvironmentTransitionPolicy` |
| **DeploymentOrchestrationContext** | Executa os rollouts técnicos no Azure Container Apps por tipo de host (Blue/Green e single-revision), consome o mesmo manifesto e executa rollbacks. | Não autoriza transições entre ambientes sem concessão formal do gate de aprovação. | DevOps / SRE | `RevisionDeployment`, `IngressTrafficRoute`, `RollbackOperation` |
| **DatabaseMigrationContext** | Executa o migrator (pelo digest imutável do manifesto) como job finito e isolado no schema `[pix]` antes da ativação dos apps em cada ambiente. | Não atende requisições de tráfego nem gerencia containers de aplicação. | DBA / DevOps | `SchemaMigrationBatch`, `MigrationLedgerRecord` |
| **CloudSecurityContext** | Fornece autenticação OIDC federada com claims segregadas por Environment (`dev`, `hml`, `prod`) e gerencia a custódia de segredos e certificados via Key Vault. | Não armazena segredos estáticos no GitHub Actions nem nos manifestos. | SecOps | `FederatedCredentialBinding`, `SecretCustody` |

---

## 2. Context Map

```
QualityAssuranceContext ──(PL: verdict)──┐
                                         ├──► ReleasePackagingContext (Core)
SchemaCompatibilityContext ─(PL: faixa)──┘              │
                                                        │ (Customer-Supplier / PL: signed manifest)
                                                        ▼
                                           PromotionGovernanceContext (Core)
                                         [Gates Manuais: Dev -> HML -> PROD]
                                                        │
                                                        │ (Downstream: authorized transition)
                                                        ▼
                         DatabaseMigrationContext ◄── DeploymentOrchestrationContext (Core)
                           (Customer-Supplier)                    ▲
                                                                  │ (OHS / PL: env-scoped OIDC)
                                                     CloudSecurityContext
```

### Relacionamentos e Padrões de Integração

```
[ReleasePackagingContext] → [PromotionGovernanceContext]
Pattern   : Published Language (PL)
Direction : upstream → downstream
Justification: O empacotamento publica o manifesto selado e assinado via Cosign, que serve de contrato imutável para toda a governança de promoção.

[PromotionGovernanceContext] → [DeploymentOrchestrationContext]
Pattern   : Customer-Supplier
Direction : Governance (upstream) autoriza Deployment (downstream)
Justification: O DeploymentOrchestrationContext só recebe permissão para implantar em Homologação ou Produção após emissão do HumanApprovalGrant assinado pelos revisores do Environment.

[CloudSecurityContext] → [DeploymentOrchestrationContext]
Pattern   : Open Host Service (OHS) / Published Language (PL)
Direction : upstream → downstream
Justification: O Entra ID valida o token OIDC escopado estritamente ao Environment autorizado pelo gate (`dev`, `hml` ou `prod`).

[DeploymentOrchestrationContext] → [DatabaseMigrationContext]
Pattern   : Customer-Supplier
Direction : Deployment aciona Migration
Justification: O deployment invoca o job do Pix.Migrator no ambiente alvo utilizando o digest registrado no manifesto antes de ativar novas revisões.
```

---

## 3. Core Domain Highlight

```
Context   : PromotionGovernanceContext
Reason    : Elimina o risco financeiro de deploys automáticos em ambientes críticos; garante auditoria humana formal de Tech Leads e Release Managers na promoção sequencial Dev -> HML -> PROD.
Investment: Regras rígidas de Environment Protection Rules, bloqueio de saltos de ambiente (proibido Dev -> Prod direto) e exigência de comprovação de testes em cada estágio.

Context   : ReleasePackagingContext
Reason    : Garante imutabilidade absoluta e integridade criptográfica da cadeia de suprimentos (Supply Chain Security).
Investment: Assinatura keyless Cosign, catálogo com os 6 digests canônicos, versão monotônica e faixa de schema compatível.

Context   : DeploymentOrchestrationContext
Reason    : Protege a disponibilidade em tempo de execução através de rollout Blue/Green nas APIs e substituição segura dos workers.
Investment: Resiliência operacional, verificação contínua de readiness probes e rollback imediato validado por schema.
```

---

## 4. Architectural Decisions

### ADR-SR-01: Manifesto Imutável de 6 Artefatos com Assinatura Cosign
* **Decision:** Compilar uma única vez no merge da `main` os 5 microsserviços e o `Pix.Migrator`. O manifesto é selado com versão monotônica, faixa `schemaMin`/`schemaMax` e assinado de forma keyless via Cosign no ACR.
* **Context:** Nenhum binário pode ser recompilado entre ambientes para garantir paridade absoluta e proteção contra ataques de supply chain.
* **Consequences:** Garante rastreabilidade total; exige verificação de assinatura Cosign antes de qualquer promoção ou rollback.

### ADR-SR-02: Promoção Sequencial em 3 Estágios com Gates Manuais de Aprovação
* **Decision:** O deploy automático é estritamente restrito ao ambiente de Desenvolvimento (`dev`). As promoções para Homologação (`hml`) e Produção (`prod`) exigem obrigatoriamente a aprovação manual de revisores designados via GitHub Environment Protection Rules. É proibido qualquer salto direto de Dev para Produção.
* **Context:** Em sistemas financeiros (Plataforma Pix), deploys automáticos em produção geram riscos inaceitáveis de indisponibilidade contábil e liquidação inconsistente no SPI.
* **Consequences:** Garante governança humana e auditoria formal; exige aprovação prévia em HML antes de liberar o gate de Produção.

### ADR-SR-03: Promoção do Mesmo Artefato Imutável entre Ambientes
* **Decision:** Os ambientes de Homologação e Produção recebem estritamente os mesmos 6 digests SHA-256 testados e validados em Desenvolvimento, sem novas compilações.
* **Context:** Reconstruir imagens entre estágios anula a garantia dos testes realizados nos estágios anteriores.
* **Consequences:** Certeza de que o artefato aprovado pelos revisores é idêntico ao testado; requer OIDC com permissão apenas de leitura de imagens e deployment no cluster alvo.

### ADR-SR-04: Rollout Assimétrico por Tipo de Host
* **Decision:** APIs com Ingress HTTP utilizam Blue/Green (revisão candidata com 0% de tráfego $\to$ probes OK $\to$ 100%). Workers operam em Single Revision com tolerâncias atreladas ao timeout de parceiros (sessões Service Bus) e TTL de leases CAS (polling no banco).
* **Context:** Workers de fila e workers de lease não possuem balanceamento HTTP e sofrem duplicidade se operarem em múltiplas revisões simultâneas.
* **Consequences:** Elimina processamento duplicado de comandos Pix e garante transição de tráfego sem queda nas rotas HTTP.

### ADR-SR-05: Zero-Secret e Segredos por Referência Versionless no Key Vault
* **Decision:** Runners usam apenas OIDC efêmero por Environment. Aplicações resolvem segredos em runtime via Managed Identity por referência versionless, com rotação por credenciais de dupla validade.
* **Context:** Segredos em texto plano no repositório violam normas bancárias; travar versão de segredo no manifesto impede rotação de emergência sem novo release.
* **Consequences:** Rotação de segredos desvinculada de release e rollbacks protegidos contra quebra de credenciais.

### ADR-SR-06: Paridade de Ambiente de CI e Banco de Dados
* **Decision:** Os testes automatizados no CI executam contra containers reais de SQL Server, Redis e Service Bus Emulator fixados por digest, eliminando o PostgreSQL legado do pipeline.
* **Context:** A autoridade transacional é o Azure SQL Server com T-SQL (`UPDLOCK, READPAST`); testar contra outros bancos mascara falhas de concorrência e sintaxe.
* **Consequences:** O quality gate no PR passa a ser uma prova válida para o aceite de código no repositório.
