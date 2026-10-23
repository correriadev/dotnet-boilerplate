# Strategic Design — Problem Space: Governança de Entrega e Retrocompatibilidade Multiambiente

## 1. Event Storming

A tabela abaixo descreve o fluxo temporal rigoroso dos eventos de domínio que regem o ciclo de entrega contínua, governança de banco de dados e validação de retrocompatibilidade entre ambientes:

| # | Domain Event (past tense) | Command (trigger) | Aggregate | External Systems | Read Models |
|---|---|---|---|---|---|
| E01 | Release Package Sealed | Seal Release Package | ReleaseManifest | Container Registry (ACR), Cosign | Release Manifest View |
| E02 | Environment Lock Acquired | Acquire Deployment Lock | EnvironmentDeployment | Azure Key Vault | Lock State Projection |
| E03 | Legacy Stable Revision Captured | Capture Stable Baseline | EnvironmentDeployment | Azure Container Apps Control Plane | Active Revision View |
| E04 | Database Schema Expanded | Apply Forward Migration | DatabaseMigration | Azure SQL Database | Schema History Projection |
| E05 | Candidate Revision Provisioned | Provision Candidate Revision | EnvironmentDeployment | Azure Container Apps Control Plane | Revision Status View |
| E06 | Candidate Readiness Verified | Probe Candidate Endpoint | QualityGate | HealthCheck Subsystem (Revision FQDN) | Candidate Health Report |
| E07 | Legacy Functional Coexistence Verified | Execute Backward Compatibility Test | BackwardCompatibilityGate | Azure SQL Database, HealthCheck Subsystem | Compatibility Audit Report |
| E08 | Ingress Traffic Converged | Comutate Traffic Weights | TrafficRouting | Azure Container Apps Envoy Ingress | Ingress Traffic Matrix |
| E09 | Deployment State Committed | Persist Environment State | DeploymentState | Azure Key Vault | Deployment State Ledger |
| E10 | Environment Lock Released | Release Deployment Lock | EnvironmentDeployment | Azure Key Vault | Lock State Projection |
| E11 | Promotion Dispatched | Dispatch Promotion Run | EnvironmentPromotion | GitHub Actions Workflow Engine | Promotion Run Ledger |

---

## 2. Subdomain Classification

| Subdomain | Type | Justification |
|---|---|---|
| **Retrocompatibilidade de Dados (Expand & Contract)** | Core | Diferencial crítico para evitar indisponibilidade e inconsistência em caso de rollback, exigindo testes funcionais de leitura/escrita do binário $N-1$ sobre o schema $N$. |
| **Governança de Implantação e Tráfego (Canary & Blue/Green)** | Core | Minimiza o blast radius de incidentes e coordena convergência progressiva de tráfego com base em telemetria objetiva. |
| **Integridade e Proveniência de Artefatos (Supply Chain)** | Supporting | Assegura que o binário testado em desenvolvimento seja o byte exato promovido até produção com atestação criptográfica. |
| **Gestão de Identidade e Segredos (Zero-Trust OIDC)** | Generic | Autenticação federada por ambiente e segredos centralizados gerenciados pelo Azure Entra ID e Azure Key Vault. |

---

## 3. Ubiquitous Language Glossary

| Term | Definition | Notes |
|---|---|---|
| **Revisão Candidata ($N$)** | A nova versão da aplicação provisionada inicialmente com 0% de tráfego público e avaliada em seu endpoint dedicado (`<app>--rel-X`). | Não expor a tráfego geral antes da validação de prontidão. |
| **Revisão Estável ($N-1$)** | A versão da aplicação que atende o tráfego do ambiente, capturada obrigatoriamente antes do provisionamento da candidata. | Destino seguro de rollback caso a migração ou a candidata apresentem anomalias. |
| **Expand-and-Contract** | Padrão evolucionário onde alterações de banco são aditivas (colunas anuláveis ou com defaults), permitindo coexistência operacional de $N-1$ e $N$. | Proíbe terminantemente `DOWN` scripts destrutivos automatizados. |
| **Gate de Retrocompatibilidade** | Ponto de controle em DEV que valida se a versão $N-1$ continua realizando operações essenciais de leitura e escrita contra o schema recém-migrado ($N$). | Uma simples checagem de conectividade SQL (`SELECT 1`) é insuficiente para atestar retrocompatibilidade. |
| **Convergência de Tráfego** | O processo gradual e observável pelo qual o Envoy Ingress redistribui novas requisições e drena conexões em trânsito. | Substitui a falsa premissa de "comutação instantânea/atômica". |
| **Manifesto Selado** | Documento imutável assinado criptograficamente contendo os digests SHA256 de todas as imagens que compõem o release. | Arquivo canônico `release-manifest.json`. |
| **Isolamento de Blast Radius** | Confinamento do impacto de incidentes e operações através da combinação de credenciais federadas OIDC por ambiente e nomenclatura explícita CAF. | Nomenclatura sozinha é cosmética sem segregação estrita de RBAC/Assinatura. |
| **Registro Canônico de Estado** | Objeto JSON atômico persistido no Key Vault contendo versão, revisões ativa/anterior, digests, ID da run e status operacional. | Evita registros desconexos sujeitos a inconsistência parcial. |
| **Bypass de Bootstrap (Cold Start)** | Tratamento explícito e restrito a ambientes que possuem zero revisões prévias provisionadas no Azure Container Apps. | Não confundir com falha de leitura de estado no Key Vault. |
