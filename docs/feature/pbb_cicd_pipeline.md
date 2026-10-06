---
doc_type: feature
domain: delivery
stack: [GitHub Actions, Azure Container Apps, Azure Key Vault, Azure SQL, ACR, Docker, Bicep, Cosign]
node_id: "feature:pbb-cicd-pipeline"
tags: [cicd, github-actions, pbb, continuous-delivery, approval-gates, oidc, key-vault, rollback]
edges:
  - relation: implements
    target: "adr:cicd"
    read: must
  - relation: depends_on
    target: "feature:qa-pipeline"
    read: must
  - relation: references
    target: "adr:container-apps"
    read: should
updated: 2026-10-06
revision: 3 (correção crítica: eliminação de promoção automática e adoção de gates manuais em 3 estágios)
---

# PBB 1 — Esteira de CI/CD e Publicação com Gates de Aprovação no GitHub Actions

Backlog estruturado segundo a metodologia Product Backlog Building (PBB) para a esteira de entrega contínua (Continuous Delivery) da Plataforma Pix.

> [!IMPORTANT]
> **Revisão 3 (Correção Crítica de Governança):** Fica estritamente **proibida qualquer promoção automática entre ambientes**. 
> O deploy é automático **exclusivamente para o ambiente de Desenvolvimento (`dev`)**. 
> A promoção para **Homologação (`hml`)** e, subsequentemente, para **Produção (`prod`)** exige a aprovação manual formal de revisores designados (Tech Lead, SRE, Release Manager) via GitHub Environment Protection Rules, promovendo rigorosamente o **MESMO artefato imutável (6 digests SHA-256)** validado no estágio anterior.

---

## 1. Product
* **Nome do Produto:** Esteira de CI/CD e Publicação com Gates de Aprovação da Plataforma Pix no GitHub Actions.
* **Objetivo:** Automatizar a verificação contínua de qualidade (pirâmide de testes de QA), empacotamento imutável de todos os artefatos executáveis (5 hosts + migrator), governança de segurança por OIDC federado com Microsoft Entra ID, consumo seguro de segredos no Azure Key Vault via Managed Identity, **deploy automático estrito para Desenvolvimento** e **promoção sequencial governada por aprovação humana para Homologação e Produção**, garantindo rollback determinístico com compatibilidade de schema comprovada.
* **Fronteiras e Escopo:** Cobre o fluxo de publicação desde a abertura de Pull Requests até o deployment em três estágios segregados no Azure: Desenvolvimento (`dev`), Homologação (`hml`) e Produção (`prod`) para os 5 microsserviços (`Pix.CashOut.Api`, `Pix.Payment.Api`, `Outbank.Connector`, `Pix.Reconciliation.Worker`, `Webhook.Dispatcher`) e a imagem de migração de banco (`Pix.Migrator`). Não engloba regras de negócio internas do Pix nem o provisionamento da VNet corporativa (escopo do PBB 2).

## 2. Problems
* **P01 — Risco de regressão e quebra de regras financeiras:** Integrações sem validação automática rigorosa podem quebrar invariantes de saldo, regras de concorrência ou o perímetro OIDC (HTTP 401).
* **P02 — Falta de imutabilidade e divergência entre ambientes:** Recompilar imagens entre Dev, Homologação e Produção cria discrepâncias de binários e impede a rastreabilidade exata dos artefatos em produção.
* **P03 — Exposição de credenciais e certificados nos runners:** Armazenar senhas, connection strings e chaves privadas mTLS no repositório expõe a infraestrutura a vazamentos e ataques de supply chain.
* **P04 — Atualização destrutiva de banco e conflito entre workers:** Migrações concorrentes sem isolamento ou sobreposição de workers disputando sessões do Service Bus ou leases CAS no banco causam processamento duplicado e locks órfãos.
* **P05 — Lentidão e incerteza no rollback:** Falhas em produção exigem tempo excessivo para reconstruir código ou reverter manualmente, elevando o MTTR.
* **P06 — Ambiente de CI diferente do runtime de nuvem:** Testes rodando contra emuladores ou bancos não equivalentes ao Azure SQL geram falso positivo no quality gate.
* **P07 — Promoção desgovernada e risco financeiro de deploy automático:** A promoção automática entre ambientes sem homologação de negócio e aprovação humana expõe contas e liquidações do SPI a falhas catastróficas em produção.
* **P08 — Rollback presumido, não comprovado:** Confiar no padrão expand/contract sem validação prévia de compatibilidade bidirecional transforma o rollback em indisponibilidade de schema.

## 3. Expectations
* **E01 — Quality Gate automatizado em 100% dos Pull Requests:** Todo PR valida lint, build Release sem warnings, executa a pirâmide completa sem falhas e sem testes ignorados contra containers reais (SQL Server, Redis, Service Bus) com cobertura global $\ge 85\%$ e por camada.
* **E02 — Imutabilidade absoluta via Manifesto de Release e Assinatura Cosign:** Build único por commit na `main`; o manifesto sela os 6 artefatos executáveis (5 hosts + migrator), recebe assinatura digital keyless via Cosign e é promovido sem alteração de binário através dos 3 ambientes (`dev` $\to$ `hml` $\to$ `prod`).
* **E03 — Zero-Secret e segredos via Key Vault:** O GitHub Actions não armazena credenciais; usa apenas OIDC efêmero por ambiente. Os hosts resolvem segredos em runtime via Managed Identity por referência versionless, com rotação por dupla validade.
* **E04 — Deploy automático exclusivo em Dev e Gates Manuais de Aprovação para HML e PROD:** 
  1. Merge em `main` faz deploy automático apenas em `dev`;
  2. Sucesso em `dev` habilita o gate de aprovação manual para `hml`;
  3. Homologação concluída com sucesso em `hml` habilita o gate de aprovação manual para `prod`;
  4. Nenhuma promoção para `prod` é permitida sem passar por `hml` e sem aprovação formal de revisores autorizados.
* **E05 — Implantação progressiva por tipo de host:** Migração como job finito pré-deploy; APIs em Blue/Green com probes; workers de sessão Service Bus e workers de lease CAS com estratégias de desligamento atreladas ao timeout do parceiro e TTL do lease.
* **E06 — Rollback determinístico sem rebuild:** Reversão para o manifesto saudável anterior em minutos, condicionada à verificação de que o schema atual está dentro da faixa `schemaMin`/`schemaMax` do manifesto restaurado.

## 4. Personas
* **Persona 1: Engenheiro de Software / Desenvolvedor Pix**
  * *Objetivo:* Feedback imediato sobre testes e qualidade no PR, e validação funcional rápida em Dev.
  * *Interações:* Abre PRs, analisa relatórios de testes/cobertura e valida correções em Dev.
* **Persona 2: Engenheiro DevOps / SRE**
  * *Objetivo:* Garantir esteiras determinísticas, seguras, ordenadas, com OIDC segregado e capacidade de reversão instantânea.
  * *Interações:* Mantém os workflows, supervisiona rollouts, atua na aprovação operacional e aciona rollbacks.
* **Persona 3: Tech Lead / Release Manager**
  * *Objetivo:* Governar a liberação de versões para Homologação e Produção com base em evidências formais de qualidade e auditoria.
  * *Interações:* Analisa relatórios de testes de Dev e HML e executa a aprovação manual nos environments do GitHub Actions.
* **Persona 4: DBA**
  * *Objetivo:* Garantir a integridade das migrações expand/contract no schema `[pix]` e a compatibilidade reversa do banco.
  * *Interações:* Homologa alterações de schema e valida a faixa de compatibilidade para rollback.

## 5. Functionalities
* **F01 — Validar Qualidade e Segurança em Pull Requests (CI & Quality Gate)**
* **F02 — Empacotar, Assinar e Auditar Artefatos Imutáveis (Package, Scan, Cosign & Manifest)**
* **F03 — Executar Deploy Contínuo em Desenvolvimento (Deploy Dev & Health Probes)**
* **F04 — Promover para Homologação com Gate de Aprovação Manual (Gate HML & Contract Validation)**
* **F05 — Promover para Produção com Gate de Aprovação Manual (Gate PROD & Progressive Delivery)**
* **F06 — Executar Reversão Determinística Validada (Rollback & Fast Recovery)**
* **F07 — Validar e Aplicar Infraestrutura como Código (IaC Governance & What-If)**

## 6. Product Backlog
* **PBI01 (F01):** Como Desenvolvedor Pix, quero que todo PR execute a pirâmide de testes completa contra containers reais (SQL Server, Redis e Service Bus Emulator) e falhe se houver qualquer teste com falha ou ignorado, para impedir regressões.
* **PBI02 (F01):** Como Desenvolvedor Pix, quero ver no PR o relatório de cobertura global e por camada (Domain 90%, Application 85%, Infrastructure 80%, Global 85%) para assegurar as metas de qualidade antes do merge.
* **PBI03 (F01):** Como Engenheiro DevOps, quero validar o build paralelo dos 6 Dockerfiles (5 hosts + migrator) no PR, sem push, para certificar a compilação dos containers.
* **PBI04 (F02):** Como Engenheiro DevOps, quero autenticar os jobs de release no Azure via OIDC federado com Entra ID, sem senhas de longa duração, para garantir Zero-Secret no GitHub Actions.
* **PBI05 (F02):** Como Engenheiro DevOps, quero compilar uma única vez no merge da `main` os 5 hosts e o `Pix.Migrator`, publicá-los no ACR, assiná-los via Cosign e gerar um `release-manifest.json` com os 6 digests SHA-256, versão monotônica e faixa de schema compatível (`schemaMin`/`schemaMax`).
* **PBI06 (F02):** Como Tech Lead, quero relatórios de vulnerabilidade (Trivy) e SBOM (Syft) para os 6 artefatos de cada release, bloqueando vulnerabilidades críticas/altas sem exceção revisada.
* **PBI07 (F03):** Como Engenheiro DevOps, quero que todo merge na `main` execute deploy automático **exclusivamente no ambiente de Desenvolvimento (`dev`)**, disparando o job finito do migrator e atualizando as revisões dos serviços com validação de health probes.
* **PBI08 (F03):** Como Desenvolvedor Pix, quero que o deploy em Dev execute smoke tests perimetrais (OIDC 401 e transações válidas via token emitido em runtime) para comprovar a saúde funcional do release.
* **PBI09 (F04):** Como Tech Lead, quero que a conclusão bem-sucedida de Dev desbloqueie um **gate de aprovação manual obrigatória** no GitHub Actions para o Environment `hml`, exigindo ação humana explícita para autorizar o deploy em Homologação.
* **PBI10 (F04):** Como Engenheiro DevOps, quero que a esteira de Homologação promova **rigorosamente os mesmos 6 digests SHA-256 e o mesmo manifesto validado em Dev**, sem recompilação de código, sob credenciais OIDC restritas ao Environment `hml`.
* **PBI11 (F04):** Como Engenheiro de QA, quero que o ambiente de Homologação execute a suíte de testes de integração estendida, testes de contrato com simuladores de parceiros (Outbank/Bacen) e validação de carga preliminar.
* **PBI12 (F05):** Como Release Manager, quero que o sucesso dos testes em Homologação desbloqueie um **gate de aprovação manual obrigatória** no GitHub Actions para o Environment `prod`, impedindo qualquer publicação acidental ou automática em Produção.
* **PBI13 (F05):** Como Engenheiro DevOps, quero que a esteira de Produção verifique a assinatura digital Cosign do manifesto, confirme a identidade dos 6 digests validados em HML, execute a migração expand/contract e realize o rollout Blue/Green nas APIs e substituição graciosa dos workers (atrelada ao TTL de lease e timeout de parceiro).
* **PBI14 (F05):** Como Engenheiro de Segurança, quero que os hosts em todos os ambientes obtenham connection strings e certificados mTLS do Key Vault via Managed Identity por referência versionless, com rotação por dupla validade.
* **PBI15 (F06):** Como Engenheiro DevOps, quero um workflow de rollback disparável sob demanda que restaure os digests do manifesto anterior sem rebuild, **recusando a reversão se o schema atual estiver fora da faixa `schemaMin`/`schemaMax` do manifesto alvo**.
* **PBI16 (F06):** Como DBA, quero que alterações de banco comprovem em PR que a revisão N-1 executa com sucesso contra o schema N, e que migrações destrutivas (contract) sejam bloqueadas na mesma release do expand correspondente.
* **PBI17 (F07):** Como Engenheiro DevOps, quero validar alterações em arquivos Bicep com lint e `what-if` para analisar impactos antes de qualquer aplicação em infraestrutura.

## 7. Traceability

| Problema / Expectativa | Persona | Funcionalidade | PBI |
| :--- | :--- | :--- | :--- |
| P01 / E01 (Quality Gate no PR) | Desenvolvedor Pix | F01 | PBI01, PBI02, PBI03 |
| P02, P03 / E02, E03 (Imutabilidade, OIDC, Cosign) | Engenheiro DevOps / Tech Lead | F02 | PBI04, PBI05, PBI06 |
| P07 / E04 (Deploy Automático em Dev) | Desenvolvedor / DevOps | F03 | PBI07, PBI08 |
| P07 / E04 (Gate de Aprovação Manual HML) | Tech Lead / QA / DevOps | F04 | PBI09, PBI10, PBI11 |
| P07 / E04, E05 (Gate de Aprovação Manual PROD) | Release Manager / DevOps / Segurança | F05 | PBI12, PBI13, PBI14 |
| P05, P08 / E06 (Rollback com Schema Validado) | DevOps / DBA | F06 | PBI15, PBI16 |
| P01, P04 / E05 (IaC Governance) | Engenheiro DevOps | F07 | PBI17 |

## 8. Assumptions
* O repositório no GitHub possui os GitHub Environments `dev`, `hml` e `prod` configurados.
* Os Environments `hml` e `prod` possuem **Environment Protection Rules ativadas** exigindo revisores obrigatórios designados (Tech Lead, SRE, Release Manager) para autorizar a execução dos jobs de deploy.
* O Microsoft Entra ID possui credenciais federadas OIDC segregadas por subject:
  * `repo:<org>/<repo>:environment:dev`
  * `repo:<org>/<repo>:environment:hml`
  * `repo:<org>/<repo>:environment:prod`
* Nenhuma imagem Docker é recompilada após o estágio de empacotamento na `main`; os 6 digests do `release-manifest.json` trafegam imutáveis por Dev, HML e PROD.
* A migração de banco no schema `[pix]` é executada pelo `Pix.Migrator` via ACA Job exclusivo antes da ativação das novas revisões em cada ambiente.

## 9. Open Questions

### Q01 — Qual mecanismo de governança e aprovação deve ser adotado para a promoção entre ambientes no GitHub Actions?

**Suggested answer:** Deploy automático restrito a Desenvolvimento (`dev`), seguido por gates de aprovação manual obrigatória via GitHub Environment Protection Rules para Homologação (`hml`) e Produção (`prod`), promovendo rigorosamente o mesmo manifesto de digests imutáveis.

**Rationale:** Elimina qualquer risco de deploy automático desgovernado em ambientes produtivos ou de homologação, garante conformidade e auditoria humana prévia para sistemas financeiros de missão crítica (SPB/Bacen) e assegura que o software testado em Dev seja exatamente o homologado e colocado em produção.

**Resolved answer:** **Aprovação Manual Formal em 3 Estágios:** o deploy contínuo automático é restrito a `dev`. Se todos os testes e probes passarem em `dev`, a pipeline libera a solicitação de aprovação manual para `hml`. Após testes de homologação aprovados, libera a solicitação de aprovação manual para `prod`. Em todas as etapas, promove-se estritamente o mesmo manifesto imutável (6 digests SHA-256) com OIDC segregado por Environment.

**Answered by:** human

**Status:** Human validated

---

### Q02 — Como a esteira deve gerenciar e consumir segredos e certificados do Key Vault sem expor credenciais?

**Suggested answer:** Zero-Secret via OIDC; hosts consomem segredos em runtime via Managed Identity e Key Vault References versionless, com rotação por dupla validade.

**Rationale:** Desvincula a rotação de segredos de um novo deploy de aplicação e impede que rollbacks quebrem por credenciais antigas revogadas prematuramente.

**Resolved answer:** Zero-Secret via OIDC com Entra ID; hosts consomem segredos via Managed Identity e Key Vault References versionless (sempre versão ativa) com rotação por dupla validade (PBI14).

**Answered by:** human

**Status:** Human validated

---

### Q03 — Qual critério deve anteceder a liberação da aprovação para o próximo ambiente?

**Suggested answer:** 100% de sucesso nos health probes (`/health/live`, `/health/ready`), ausência de restarts dos workers e aprovação dos testes de fumaça e integração do ambiente atual.

**Rationale:** Garante que a aprovação manual só seja desbloqueada para a liderança técnica após comprovação automatizada de saúde e prontidão do ambiente corrente.

**Resolved answer:** 100% dos testes, health probes e testes de integração/fumaça aprovados no ambiente corrente sem anomalias, liberando formalmente a ação de aprovação manual na interface da pipeline para os revisores.

**Answered by:** human

**Status:** Human validated

---

### Q04 — O manifesto deve ser assinado digitalmente (Cosign) e verificado antes da promoção?

**Suggested answer:** Sim, assinatura keyless via OIDC do GitHub (Cosign) com verificação mandatória antes de qualquer deploy em HML e PROD e em operações de rollback.

**Rationale:** Garante a integridade da cadeia de suprimentos e impede que manifestos adulterados ou não originados da branch `main` oficial sejam aprovados para execução.

**Resolved answer:** Assinatura digital keyless via **Cosign** integrada ao OIDC; a verificação criptográfica da assinatura do manifesto e das imagens é pré-requisito mandatório antes de aplicar os deploys em `hml` e `prod` e no `rollback.yml`.

**Answered by:** human

**Status:** Human validated
