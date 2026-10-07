# 002 — Strategic Design: Context Map
**Domínio:** `observabilidade_dynatrace`  
**Data:** 2026-10-07  
**Status:** Aprovado  

---

## 1. Bounded Context Identification

| Bounded Context | Responsabilidade | Fronteira (Escopo Excluído) | Time / Propriedade | Entidades-Chave |
|---|---|---|---|---|
| **Governança de Confiabilidade & Quality Gates** | Avalia a conformidade de releases através de SLIs/SLOs e do Site Reliability Guardian (SRG), emitindo laudos de aprovação ou bloqueio para as esteiras de entrega contínua e vigiando a janela de bake em Produção. | Não executa testes de carga em si (escopo de QA); não comuta tráfego de rede nem orquestra containers (escopo de CI/CD). | Time de SRE & Release Management | `LaudoGuardian` (Root), `ObjetivoNivelServico` (SLO), `EventoSDLC`, `InspecaoBakePeriod`, `VereditoQualityGate`. |
| **Telemetria & Rastreamento Distribuído** | Captura, enriquece com metadados W3C e propaga spans e métricas de forma resiliente e não-bloqueante através das camadas HTTP, Outbox SQL e filas do Service Bus. | Não armazena séries temporais permanentemente (escopo do Dynatrace); não altera regras de negócio de pagamentos Pix. | Time Core Pix & Arquitetura | `EnvelopeTransacional` (Root), `ContextoW3C`, `SpanExecucao`, `ExportadorLoteOTLP`. |
| **Proteção & Higienização de Dados** | Garante que nenhuma Informação Pessoal Identificável (PII) ou dado sob sigilo bancário trafegue na telemetria, aplicando regras de filtro na origem e regras de redação. | Não gerencia chaves criptográficas no Key Vault; não autentica usuários no Entra ID. | Time de Segurança (SecOps) & Compliance | `RegraSanitizacao` (Root), `IdentificadorTecnico`, `MascaraRedacao`. |
| **Dynatrace APM SaaS & Azure Monitor (Externo)** | Ingestão em larga escala de OTLP e métricas PaaS, armazenamento em Grail, análise causal da Davis AI e consolidação de Smartscape. | Sistema externo gerenciado (SaaS/PaaS). | Fornecedor Externo (Dynatrace / Azure) | `DavisAiProblem`, `SmartscapeTopology`, `AzurePaaSMetricStream`. |
| **Pipeline de Entrega Contínua (CI/CD GitHub Actions)** | Orquestra a execução de builds, publicação de artefatos imutáveis e execução de gates de aprovação em `dev`, `hml` e `prod`. | Não toma decisões de negócio de observabilidade além de consultar os laudos de aprovação. | Time DevOps | `ReleaseManifest`, `WorkflowRun`, `EnvironmentApprovalGate`. |

---

## 2. Context Map

### Relacionamentos entre Bounded Contexts

```
[Governança de Confiabilidade & Quality Gates] (Upstream) ──[Customer-Supplier]──> [Pipeline de Entrega Contínua] (Downstream)
Pattern   : Customer-Supplier
Direction : Upstream (Governança/SRG) / Downstream (GitHub Actions)
Justification: O GitHub Actions depende do laudo de avaliação do SRG para cumprir a política mandatória de bloquear a aprovação de releases degradados.
```

```
[Telemetria & Rastreamento Distribuído] (Upstream) ──[Open Host Service / Published Language]──> [Dynatrace APM SaaS] (Downstream)
Pattern   : Open Host Service / Published Language (OTLP W3C)
Direction : Upstream (Aplicações Pix) / Downstream (Dynatrace SaaS)
Justification: As aplicações .NET 8 exportam telemetria aderente ao protocolo universal OTLP sem dependência de bibliotecas proprietárias nos containers.
```

```
[Proteção & Higienização de Dados] (Upstream) ──[Shared Kernel]──> [Telemetria & Rastreamento Distribuído] (Downstream)
Pattern   : Shared Kernel
Direction : Bidirectional / Co-evolução
Justification: A captura de spans compartilha os tipos de identificadores técnicos higienizados e os filtros de exclusão de campos sensíveis para evitar vazamento de PII.
```

```
[Governança de Confiabilidade & Quality Gates] (Downstream) ──[Anti-Corruption Layer (ACL)]──> [Dynatrace APM SaaS] (Upstream)
Pattern   : Anti-Corruption Layer (ACL)
Direction : Downstream (Governança) / Upstream (Dynatrace SaaS)
Justification: O contexto de governança isola as chamadas às APIs REST do Dynatrace (Guardian Evaluations, Davis Problems), traduzindo payloads proprietários para os modelos ubíquos de LaudoGuardian e InspecaoBakePeriod.
```

---

## 3. Core Domain Highlight

```
Context   : Governança de Confiabilidade & Quality Gates
Reason    : Representa o mecanismo central de garantia de estabilidade, conformidade financeira B3/BACEN e proteção contra indisponibilidade catastrófica no Pix. Automatiza decisões críticas de bloqueio de promoção em Homologação e salvaguarda a janela de bake em Produção com evidências determinísticas.
Investment: Modelagem tática rigorosa com Aggregates ricos, invariantes de transição de estado estritas (PASS/FAIL/WARNING), políticas de timeout de avaliação, e desacoplamento via Anti-Corruption Layer (ACL) para não se tornar refém de contratos de APIs externas.
```

---

## 4. Architectural Decisions

### ADR-OBS-01 — Bloqueio Automático Estrito em Homologação via Site Reliability Guardian (SRG)
* **Decisão:** A reprovação de SLOs pelo Dynatrace SRG ao término dos testes de carga na esteira `promote-hml.yml` falha automaticamente o job de homologação e bloqueia a liberação do gate manual de aprovação para Produção.
* **Contexto:** Necessidade de eliminar aprovações de release baseadas em intuição, garantindo que código com vazamento de memória ou violação de latência não alcance produção.
* **Consequências:** (+) Impede regressões de performance em produção de forma determinística; (+) Fornece feedback imediato com evidências no GitHub Step Summary; (-) Exige calibração rigorosa dos limiares de SLO em Homologação para evitar falsos positivos que bloqueiem a esteira.

### ADR-OBS-02 — Propagação do Contexto W3C no Envelope Transacional do Outbox e Service Bus
* **Decisão:** Persistir os metadados `TraceParent` e `TraceState` como colunas explícitas no `[pix].[OutboxRecords]`, repassá-los nas `ApplicationProperties` do Azure Service Bus e restaurá-los no `ActivitySource` dos Workers.
* **Contexto:** Garantir que o Dynatrace mantenha a visão unificada da transação financeira (*PurePath*) sem gerar *spans* órfãos nos saltos assíncronos de banco e mensageria.
* **Consequências:** (+) Visibilidade ponta a ponta desde a API até a liquidação no BACEN; (+) Diagnóstico exato de latência em cada estágio assíncrono; (-) Adiciona colunas adicionais de metadados na tabela de Outbox e sobrecarga insignificante no payload de mensageria.

### ADR-OBS-03 — Resiliência Fail-Open Não-Bloqueante com Descarte Silencioso na Telemetria OTLP
* **Decisão:** O pipeline de exportação OTLP das aplicações .NET 8 opera em background com buffer bounded em memória (`BatchExportProcessor`). Em caso de lentidão ou indisponibilidade do Dynatrace SaaS, os spans e métricas excedentes são descartados silenciosamente (*drop*).
* **Contexto:** A indisponibilidade de ferramentas de observabilidade nunca pode degradar a latência, consumir memória descontroladamente ou rejeitar transações de pagamento dos clientes.
* **Consequências:** (+) Isolamento completo da disponibilidade da API Pix em relação a falhas de rede no SaaS; (+) Consumo de memória estritamente delimitado; (-) Risco de perda pontual de telemetria durante falhas prolongadas de conexão com o Dynatrace.

### ADR-OBS-04 — Higienização na Origem e Mascaramento Preventivo no SaaS para Conformidade LGPD
* **Decisão:** As aplicações .NET 8 são proibidas de injetar dados de identificação pessoal (CPF, e-mail, telefone) ou contas bancárias nas tags de Activity e logs, permitindo apenas IDs técnicos (`PedidoId`, `EndToEndId`). Em paralelo, o Dynatrace é configurado com regras de redação regex preventivas.
* **Contexto:** Atendimento mandatório à LGPD, à Resolução BCB nº 1 e à Lei de Sigilo Bancário (LC 105/2001).
* **Consequências:** (+) Blindagem jurídica e de segurança contra vazamento de dados de clientes na nuvem; (+) Auditoria simplificada; (-) Desenvolvedores não podem consultar traces buscando diretamente pelo CPF do cliente (devem correlacionar via `PedidoId` ou `EndToEndId`).

### ADR-OBS-05 — Segregação Lógica de Ambientes via Management Zones em Tenant Unificado
* **Decisão:** Centralizar os dados em uma instância única do Dynatrace, com segregação lógica estrita de ambientes via Management Zones (`MZ_PIX_DEV`, `MZ_PIX_HML`, `MZ_PIX_PROD`), tags semânticas OTel e Alerting Profiles dedicados.
* **Contexto:** Otimizar custos de licenciamento e viabilizar comparações de performance entre Homologação e Produção, sem poluir baselines de inteligência artificial nem misturar alertas de plantão.
* **Consequências:** (+) Visão corporativa centralizada e comparabilidade entre ambientes; (+) Alertas de DEV não tocam o plantão de SRE; (-) Exige disciplina rigorosa na aplicação das tags semânticas de ambiente nos deployments.
