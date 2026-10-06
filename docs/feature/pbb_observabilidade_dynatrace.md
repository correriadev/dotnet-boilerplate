---
doc_type: feature
domain: observability
stack: [Dynatrace, OpenTelemetry, Azure Container Apps, Azure Monitor, GitHub Actions, .NET 8]
node_id: "feature:pbb-observabilidade-dynatrace"
tags: [pbb, observability, dynatrace, opentelemetry, quality-gates, sli-slo, srg, environment-segregation]
edges:
  - relation: implements
    target: "adr:architecture"
    read: must
  - relation: depends_on
    target: "feature:pbb-cicd-pipeline"
    read: must
  - relation: depends_on
    target: "feature:qa-pipeline"
    read: must
  - relation: references
    target: "feature:pbb-infra-landing-zone"
    read: should
updated: 2026-10-06
---

# PBB 3 — Observabilidade de Produção e Quality Gates com Dynatrace na Plataforma Pix

Backlog estruturado segundo a metodologia Product Backlog Building (PBB) para a governança de observabilidade contínua, monitoramento de métricas em produção, segregação de ambientes e automação de Quality Gates no Dynatrace para a Plataforma Pix.

---

## 1. Product
* **Nome do Produto:** Plataforma de Observabilidade e Quality Gates com Dynatrace na Plataforma Pix.
* **Objetivo:** Estabelecer uma arquitetura de observabilidade corporativa ponta a ponta com Dynatrace e OpenTelemetry para os 5 microsserviços da Plataforma Pix (.NET 8 em Azure Container Apps), garantindo monitoramento em tempo real de Golden Signals e métricas financeiras críticas em produção, detecção de causa raiz via Davis AI, isolamento estrito entre ambientes (`dev`, `hml`, `prod`), higienização de dados segundo a LGPD e o sigilo bancário, e integração direta com o GitHub Actions como Quality Gate automatizado (Site Reliability Guardian) nas esteiras de CI/CD e QA.
* **Fronteiras e Escopo:** Abrange a telemetria distribuída dos 5 microsserviços (`Pix.CashOut.Api`, `Pix.Payment.Api`, `Outbank.Connector`, `Pix.Reconciliation.Worker`, `Webhook.Dispatcher`), métricas de infraestrutura PaaS via integração com Azure Monitor, segregação por Management Zones, avaliação de performance pós-testes de carga em Homologação, acompanhamento da janela de bake de 10 minutos pós-deploy Blue/Green em Produção e alertas operacionais. Não engloba a implementação de regras de negócio internas do domínio Pix nem o provisionamento da VNet em Bicep (cobertos nos PBBs 1 e 2).

## 2. Problems
* **P01 — Falta de visibilidade de causa raiz em fluxos distribuídos assíncronos:** A cadeia assíncrona ($\text{API} \to \text{Outbox} \to \text{Service Bus} \to \text{Worker Outbank} \to \text{SPI Bacen}$) pode sofrer lentidão ou interrupção silenciosa sem que os operadores consigam correlacionar rapidamente qual dependência ou componente gerou a falha.
* **P02 — Risco de degradação silenciosa em produção e quebra de SLA regulatório do BACEN:** Atrasos na liquidação Pix superiores a 2 segundos no SPI ou acúmulo de transações em `ResultadoIncerto` causam prejuízos financeiros e sanções do Banco Central se não detectados proativamente.
* **P03 — Aprovações manuais no CI/CD baseadas em suposições (Ausência de Quality Gate de Telemetria):** O Tech Lead e o Release Manager dependem de inspeções visuais manuais após os testes de carga de QA em Homologação, sem uma validação objetiva e automatizada que ateste se os SLOs de latência e consumo de recursos foram preservados antes de promover o release.
* **P04 — Risco de falhas críticas passarem despercebidas na janela de bake pós-deploy:** Durante a janela de 10 minutos de estabilização pós-deploy Blue/Green em Produção, regressões de latência ou aumento de erros 5xx podem passar desapercebidas antes do encerramento da revisão anterior, impedindo uma reação rápida de rollback.
* **P05 — Poluição de dados e alarmes falsos por falta de segregação entre ambientes:** Testes de carga em Homologação ou falhas em Desenvolvimento misturados aos dados de Produção geram ruído nos dashboards, corrompem os baselines de inteligência artificial (Davis AI) e geram fadiga de alertas na equipe.
* **P06 — Exposição de dados cadastrais e financeiros (PII/Sigilo Bancário) na telemetria:** Vazamento acidental de chaves Pix (CPF, e-mail, telefone), contas ou valores em traces, spans e logs enviados para a nuvem viola a LGPD e o sigilo bancário (LC 105/2001).

## 3. Expectations
* **E01 — Rastreabilidade ponta a ponta e identificação de causa raiz (Davis AI):** Cada transação Pix correlacionada de ponta a ponta via W3C Trace Context, permitindo que a IA do Dynatrace identifique anomalias e isole a causa raiz de gargalos de rede, queries SQL, locks ou chamadas externas em segundos.
* **E02 — Monitoramento 24/7 de Golden Signals e SLIs de Negócio Pix:** Dashboards em tempo real e baselines dinâmicos cobrindo Latência P95/P99, Taxa de Erro HTTP 5xx, Vazão (TPS), tempo de resposta do SPI BACEN, lag de sessões no Service Bus, vazão do OutboxRelay e status das ordens.
* **E03 — Quality Gate automatizado em Homologação (Site Reliability Guardian):** Validação automática de SLOs no GitHub Actions disparada ao término dos testes de carga de QA em `hml`, bloqueando automaticamente a liberação do gate de promoção para Produção caso os critérios de performance sejam reprovados.
* **E04 — Vigilância intensiva e alerta P1 durante a janela de bake de Produção:** Monitoramento focal da nova revisão do ACA nos 10 minutos pós-deploy Blue/Green com alerta imediato para o SRE On-Call em caso de anomalia, permitindo reversão manual informada e fundamentada com um clique.
* **E05 — Segregação rigorosa de ambientes e perfis de alerta:** Isolamento de métricas, dashboards e permissões via Management Zones (`MZ_PIX_DEV`, `MZ_PIX_HML`, `MZ_PIX_PROD`) e roteamento inteligente de alertas por criticidade de ambiente.
* **E06 — Higienização na origem e mascaramento preventivo de dados sensíveis:** Telemetria livre de dados pessoais e financeiros confidenciais, com mascaramento ativo no Dynatrace e uso exclusivo de identificadores técnicos (`PedidoId`, `EndToEndId`, `CorrelationId`).

## 4. Personas
* **Persona 1: Engenheiro de Confiabilidade (SRE) / Observabilidade**
  * *Objetivo:* Configurar coletores, dashboards, alertas, Davis AI, SLOs e gerenciar a plataforma Dynatrace.
  * *Interações:* Define SLOs, cria Management Zones, configura regras de alerta e diagnostica incidentes em produção.
  * *Relação:* Resolve P01, P02 e P05; atende E01, E02 e E05.
* **Persona 2: Engenheiro de Software / Desenvolvedor Pix**
  * *Objetivo:* Rastrear o comportamento de sua aplicação, diagnosticar queries Dapper lentas, locks de concorrência e exceções.
  * *Interações:* Consulta traces distribuídos, waterfall de spans e exceções capturadas no Dynatrace.
  * *Relação:* Resolve P01; atende E01.
* **Persona 3: Tech Lead / Release Manager**
  * *Objetivo:* Governar a promoção de releases para Produção e a operação de rollback com base em evidências objetivas de qualidade.
  * *Interações:* Analisa o relatório do Site Reliability Guardian no GitHub Actions para aprovar gates manuais e atua em caso de alerta na janela de bake.
  * *Relação:* Resolve P03 e P04; atende E03 e E04.
* **Persona 4: Engenheiro de QA / Performance**
  * *Objetivo:* Certificar que novas versões suportam a carga projetada sem violar orçamentos de erro ou critérios de latência.
  * *Interações:* Executa suítes de teste de carga em Homologação e valida os resultados do Quality Gate do Dynatrace.
  * *Relação:* Resolve P03; atende E03.
* **Persona 5: Engenheiro de Segurança / SecOps**
  * *Objetivo:* Garantir que a telemetria não trafegue nem armazene dados sensíveis de clientes ou do sigilo financeiro.
  * *Interações:* Audita tags de spans, logs de telemetria e valida as regras de mascaramento no Dynatrace.
  * *Relação:* Resolve P06; atende E06.

## 5. Functionalities
* **F01 — Ingerir Telemetria e Monitorar Infraestrutura em Nuvem (OTLP & Cloud Ingestion)**
* **F02 — Monitorar Golden Signals e Indicadores de Negócio Pix (APM & Pix SLIs)**
* **F03 — Segregar Ambientes e Governança de Acessos (Management Zones & Tagging)**
* **F04 — Executar Quality Gate Automatizado na Esteira de Homologação (SRG & CI/CD Gate)**
* **F05 — Monitorar Janela de Bake e Suportar Rollback em Produção (Bake Watcher & Davis AI)**
* **F06 — Assegurar Privacidade e Conformidade de Dados Financeiros (Data Privacy & Masking)**

## 6. Product Backlog
* **PBI01 (F01):** Como Engenheiro SRE, quero que os 5 microsserviços (.NET 8) exportem traces, métricas e logs via protocolo OTLP para o Dynatrace SaaS utilizando o OpenTelemetry SDK padronizado, para manter visibilidade profunda sem acoplar agentes proprietários aos containers no Azure Container Apps.
* **PBI02 (F01):** Como Engenheiro SRE, quero configurar a integração do Dynatrace com o Azure Monitor via credencial do Microsoft Entra ID para coletar métricas de plataforma do ACA Environment, Azure SQL, Redis e Service Bus de forma contínua.
* **PBI03 (F02):** Como Engenheiro de Software Pix, quero visualizar nos serviços de API e Workers os Golden Signals (Latência P50/P95/P99, Vazão TPS, Taxa de Erro 4xx/5xx e Saturação de CPU/Memória/Pool SQL) para diagnosticar gargalos técnicos rapidamente.
* **PBI04 (F02):** Como Especialista de Negócio Pix, quero acompanhar em dashboards dedicados os indicadores críticos de negócio (latência de liquidação SPI Bacen, taxa de ordens em `ResultadoIncerto`, lag do Service Bus e vazão do OutboxRelay) para garantir a saúde das transações financeiras.
* **PBI05 (F02):** Como Engenheiro SRE, quero estabelecer SLOs de serviço formais (Disponibilidade $\ge 99.99\%$ e Latência P95 $< 400\text{ ms}$) com rastreamento de consumo de Error Budget para governar a confiabilidade das APIs Pix.
* **PBI06 (F03):** Como Engenheiro SRE, quero que toda telemetria emitida contenha tags semânticas de ambiente (`deployment.environment: dev | hml | prod`) associadas a Management Zones dedicadas (`MZ_PIX_DEV`, `MZ_PIX_HML`, `MZ_PIX_PROD`) para segregar dashboards, acessos e evitar interferência entre estágios.
* **PBI07 (F03):** Como Engenheiro SRE, quero Alerting Profiles distintos por ambiente para que anomalias em Dev gerem apenas notificações informativas e incidentes em Prod acionem o SRE On-Call 24/7 imediatamente.
* **PBI08 (F04):** Como Engenheiro de QA, quero que ao término dos testes de carga na esteira de Homologação (`promote-hml.yml`), o GitHub Actions envie um evento SDLC ao Dynatrace para disparar a validação do Site Reliability Guardian (SRG).
* **PBI09 (F04):** Como Tech Lead, quero que o resultado do Dynatrace SRG em Homologação seja avaliado pelo GitHub Actions, falhando automaticamente o job e bloqueando a liberação do gate manual para Produção caso os SLOs sejam violados.
* **PBI10 (F05):** Como Release Manager, quero que a esteira de Produção (`promote-prod.yml`) monitore a nova revisão durante a janela de bake de 10 minutos via Dynatrace, alertando imediatamente o SRE On-Call com evidência de causa raiz (Davis AI) caso ocorra degradação.
* **PBI11 (F05):** Como Engenheiro SRE, quero que o alerta de anomalia durante o bake period forneça link direto para execução com um clique do workflow `rollback.yml`, assegurando resposta rápida e governada a incidentes de publicação.
* **PBI12 (F06):** Como Engenheiro de Segurança, quero diretrizes de instrumentação que restrinjam as tags de tracing e logs aos identificadores técnicos (`PedidoId`, `EndToEndId`, `CorrelationId`), proibindo terminantemente a emissão de CPF, chaves Pix, nomes ou dados de conta.
* **PBI13 (F06):** Como Engenheiro de Segurança, quero regras de mascaramento e redação regex configuradas no tenant do Dynatrace para sanitizar preventivamente qualquer dado financeiro ou pessoal residual em stacktraces ou logs.

## 7. Traceability

| Problema / Expectativa | Persona | Funcionalidade | PBI |
| :--- | :--- | :--- | :--- |
| P01 / E01 (Rastreabilidade e Ingestão OTLP) | SRE / Dev Pix | F01 (OTLP & Cloud Ingestion) | PBI01, PBI02 |
| P01, P02 / E01, E02 (Golden Signals e Negócio) | SRE / Dev / Negócio | F02 (APM & Pix SLIs) | PBI03, PBI04, PBI05 |
| P05 / E05 (Segregação de Ambientes e Alertas) | SRE | F03 (Management Zones & Tagging) | PBI06, PBI07 |
| P03 / E03 (Quality Gate Automatizado em HML) | Tech Lead / QA / SRE | F04 (SRG & CI/CD Gate) | PBI08, PBI09 |
| P04 / E04 (Vigilância no Bake e Rollback) | Release Manager / SRE | F05 (Bake Watcher & Davis AI) | PBI10, PBI11 |
| P06 / E06 (Privacidade e Mascaramento LGPD) | Segurança / Dev | F06 (Data Privacy & Masking) | PBI12, PBI13 |

## 8. Assumptions
* A instrumentação das aplicações adota estritamente o SDK padrão do OpenTelemetry (.NET 8) com exportação OTLP, evitando agentes proprietários dentro das imagens Docker dos microsserviços.
* O Dynatrace SaaS disponibiliza endpoint OTLP (`/api/v2/otlp`) com API Token contendo escopos de ingestão (`openTelemetryTrace.ingest`, `metrics.ingest`, `logs.ingest`).
* A integração de métricas de infraestrutura PaaS é realizada via Dynatrace Azure Monitor Integration com Service Principal gerenciado no Microsoft Entra ID.
* Os três ambientes (`dev`, `hml`, `prod`) compartilham a mesma instância Dynatrace segregados logicamente por Management Zones (`MZ_PIX_DEV`, `MZ_PIX_HML`, `MZ_PIX_PROD`).
* O Dynatrace Site Reliability Guardian (SRG) é disparado a partir de eventos SDLC enviados pelos workflows do GitHub Actions (`promote-hml.yml` e `promote-prod.yml`).
* Nenhuma informação pessoal identificável (PII), chave Pix, CPF ou credencial bancária é enviada em tags de telemetria ou logs.

## 9. Open Questions

### Q01 — Qual estratégia de instrumentação e coleta do Dynatrace deve ser adotada nos containers (.NET 8 em Azure Container Apps)?

**Suggested answer:** OpenTelemetry Nativo via OTLP com envio direto ao Dynatrace SaaS, complementado pela integração Azure Monitor para infraestrutura PaaS.

**Rationale:** Conforme documentação oficial da Dynatrace e da Microsoft, o Azure Container Apps é uma plataforma PaaS serverless onde a instalação do OneAgent clássico é desaconselhada e não suportada. O OpenTelemetry nativo é o padrão recomendado pela Dynatrace para ACA, preservando conformidade com a política de zero lock-in já aprovada na arquitetura da Plataforma Pix.

**Resolved answer:** OpenTelemetry Nativo via OTLP com SDK .NET 8 (exportação direta para o endpoint OTLP do Dynatrace SaaS) + Integração Azure Monitor no Dynatrace para métricas PaaS.

**Answered by:** human

**Status:** Human validated

---

### Q02 — Como a esteira de CI/CD do GitHub Actions deve reagir se o Dynatrace Site Reliability Guardian (SRG) reprovar a análise em Homologação (HML)?

**Suggested answer:** Bloqueio Automático do Gate: a esteira falha o job de validação e impede a habilitação do gate manual de aprovação para Produção.

**Rationale:** Impede que código com regressão de latência, vazamento de recursos ou violação de SLOs seja promovido para Produção, garantindo a eficácia do Quality Gate automatizado.

**Resolved answer:** Bloqueio Automático do Gate no GitHub Actions: falha de SLO no Dynatrace Site Reliability Guardian (SRG) em HML encerra o job com erro e impede a liberação da aprovação manual para Produção até que a regressão seja corrigida.

**Answered by:** human

**Status:** Human validated

---

### Q03 — Como o Dynatrace deve agir durante a Janela de Bake (10 minutos pós-deploy Blue/Green) em Produção (`prod`)?

**Suggested answer:** Alerta P1 Imediato ao SRE On-Call com evidência de causa raiz (Davis AI) e link para o workflow de rollback, mantendo a decisão final de reversão com o operador humano.

**Rationale:** Em ambientes bancários e de liquidação instantânea (SPB/BACEN), o acionamento humano auditado evita reversões acidentais provocadas por oscilações transitórias de conectividade externa com parceiros.

**Resolved answer:** Alerta P1 Imediato ao SRE On-Call com dados de causa raiz do Davis AI e link direto para o `rollback.yml`, assegurando resposta rápida e governada enquanto mantém a decisão final com o operador humano.

**Answered by:** human

**Status:** Human validated

---

### Q04 — Qual política de higienização e privacidade de dados (LGPD / Sigilo Bancário) deve ser aplicada na telemetria enviada ao Dynatrace?

**Suggested answer:** Higienização na Origem (.NET 8) permitindo apenas IDs técnicos correlacionáveis (`PedidoId`, `EndToEndId`), complementada por regras de mascaramento e redação regex preventivas no Dynatrace.

**Rationale:** Assegura conformidade estrita com a LGPD, regulamentações do BACEN e a Lei de Sigilo Bancário, prevenindo vazamento de dados de clientes em plataformas de observabilidade em nuvem.

**Resolved answer:** Higienização na Origem (.NET 8) com proibição estrita de PII em tags de trace e logs (apenas `PedidoId`, `EndToEndId`, `CorrelationId` permitidos), combinada com regras ativas de mascaramento regex no tenant Dynatrace.

**Answered by:** human

**Status:** Human validated
