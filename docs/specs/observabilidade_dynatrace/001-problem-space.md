# 001 — Strategic Design: Problem Space
**Domínio:** `observabilidade_dynatrace`  
**Data:** 2026-10-07  
**Status:** Aprovado  

---

## 1. Event Storming

Fluxo temporal simulado para o domínio de Observabilidade Financeira, Governança de Confiabilidade e Quality Gates da Plataforma Pix:

| # | Domain Event (past tense) | Command (trigger) | Aggregate | External Systems | Read Models |
|---|---|---|---|---|---|
| 01 | `RequisicaoPixRecebidaComContextoW3C` | `ReceberComandoHttp` | `PerimetroRastreamento` | Cliente HTTP / Gateway Cloudflare | `PainelTransacoesEmTempoReal` |
| 02 | `ContextoW3CPropagadoNoOutbox` | `PersistirTransacaoComContexto` | `EnvelopeTransacional` | Azure SQL Database (`[pix]`) | `TabelaOutboxRecords` |
| 03 | `MensagemServiceBusPublicadaComContextoW3C` | `PublicarEventoOutboxRelay` | `EnvelopeTransacional` | Azure Service Bus (FIFO Sessions) | `FilaMensagensProcessamento` |
| 04 | `ContextoW3CRestauradoNoWorker` | `ConsumirMensagemComContexto` | `WorkerExecucao` | Azure Service Bus / Outbank | `HistoricoProcessamentoWorker` |
| 05 | `SpansETelemetriaSanitizados` | `CapturarTelemetriaExecucao` | `SanitizadorTelemetria` | — | `RegistroAuditoriaPrivacidade` |
| 06 | `LoteOTLPExportadoComFailOpen` | `DespacharLoteTelemetria` | `ExportadorOTLP` | Dynatrace SaaS (`/api/v2/otlp`) | `MetricasVazaoExportador` |
| 07 | `MetricasPaaSColetadasViaAzureMonitor` | `SincronizarMetricasPlataforma` | `ColetorAzureMonitor` | Azure Monitor / Entra ID | `PainelInfraestruturaPaaS` |
| 08 | `SLOsEConsumoErrorBudgetAferidos` | `AferirIndicadoresConfiabilidade` | `IndicadorConfiabilidade` | Dynatrace API / SLI Store | `DashboardSLOProducao` |
| 09 | `AnomaliaTransacionalDetectadaPelaDavisAI` | `AvaliarPadraoComportamental` | `AnalisadorDavisAI` | Dynatrace Davis AI Engine | `PainelIncidentesProblemas` |
| 10 | `EventoSDLCDeCargaDisparado` | `NotificarFimTesteDeCarga` | `GatilhoQualityGate` | GitHub Actions (`promote-hml.yml`) | `HistoricoExecucoesEsteira` |
| 11 | `AvaliacaoSiteReliabilityGuardianExecutada` | `AvaliarGuardianHomologacao` | `SiteReliabilityGuardian` | Dynatrace SRG Engine | `LaudoAvaliacaoGuardian` |
| 12 | `QualityGateDeHomologacaoReprovado` | `ProcessarVereditoSRG` | `GateAprovacaoEsteira` | GitHub Actions Environment Rules | `GitHubJobSummary` |
| 13 | `QualityGateDeHomologacaoAprovado` | `ProcessarVereditoSRG` | `GateAprovacaoEsteira` | GitHub Actions Environment Rules | `GitHubJobSummary` |
| 14 | `JanelaDeBakeIniciadaEmProducao` | `AtivarMonitoramentoBakePeriod` | `VigilanciaBakePeriod` | Azure Container Apps / GitHub Actions | `StatusRevisaoBlueGreen` |
| 15 | `AlertaP1DeDegradacaoNoBakeEmitido` | `IdentificarRegressaoBakePeriod` | `VigilanciaBakePeriod` | Dynatrace Problem API / Teams / PagerDuty | `AlertaPlantaoSRE` |
| 16 | `RollbackManualAcionadoComBaseEmEvidencias` | `DispararRollbackManual` | `OrquestradorReversao` | GitHub Actions (`rollback.yml`) | `AuditoriaRollbackProducao` |

---

## 2. Subdomain Classification

| Subdomain | Type | Justification |
|---|---|---|
| **Governança de Confiabilidade & Quality Gates** | **Core** | Automação decisória vinculada ao manifesto criptográfico de release, bloqueio estrito em Homologação e proteção de bake em Produção para o Pix B3/Bacen. |
| **Telemetria & Rastreamento Distribuído** | **Supporting** | Garante a continuidade do W3C Trace Context através de barreiras assíncronas (Outbox e Service Bus) e resiliência fail-open para os serviços Pix. |
| **Proteção de Dados & Conformidade Regulatória** | **Supporting** | Assegura conformidade estrita com a LGPD e o Sigilo Bancário (LC 105) através de higienização na origem e regras de redação. |
| **Plataforma de Monitoramento & Inteligência APM** | **Generic** | Capacidades commoditizadas de mercado fornecidas pelo Dynatrace SaaS (Davis AI, Smartscape, ingestão OTLP) e Azure Monitor. |

---

## 3. Ubiquitous Language Glossary

| Termo | Definição de Negócio | Notas e Cuidados |
|---|---|---|
| **W3C Trace Context** | Padrão formal composto por `traceparent` (TraceId e SpanId) e `tracestate` que identifica univocamente uma transação financeira em todos os nós da malha. | Não confundir com `CorrelationId` de log; o Trace Context rege a árvore causal da transação. |
| **Site Reliability Guardian (SRG)** | Mecanismo autônomo do Dynatrace que avalia métricas e Service-Level Indicators (SLIs) de um release frente a critérios de aceitação e objetivos de nível de serviço. | Atua como árbitro bloqueante na esteira de Homologação (`promote-hml.yml`). |
| **Janela de Bake (Bake Period)** | Período de 10 minutos de observação intensiva da nova revisão em Produção (`prod`) após o deploy Blue/Green, sob tráfego real. | Durante essa janela, anomalias disparam alerta P1 imediato para o SRE On-Call. |
| **Fail-Open de Telemetria** | Diretriz que garante que a indisponibilidade ou latência do sistema de observabilidade nunca trave, retarde ou reprove transações de pagamento Pix. | Spans e métricas são descartados silenciosamente em sobrecarga de buffer; processamento financeiro é sagrado. |
| **Golden Signals Financeiros** | Conjunto composto por Latência (P50/P95/P99), Vazão (TPS), Taxa de Erro HTTP 5xx/4xx e Saturação (CPU, Memória, SQL Pool, Threads), acrescido do SLA do SPI Bacen ($< 2\text{ s}$). | Métrica mandatória para avaliação do SRG e baselines do Davis AI. |
| **Management Zone** | Particionamento lógico no Dynatrace que isola entidades, dashboards e alertas por ambiente operacional (`MZ_PIX_DEV`, `MZ_PIX_HML`, `MZ_PIX_PROD`). | Evita que testes de estresse em HML poluam baselines e disparem falsos alertas de Produção. |
| **Evento SDLC** | Notificação padronizada emitida pelo GitHub Actions para o Dynatrace sinalizando a transição de ciclo de vida (ex: fim do teste de carga). | Deve carregar obrigatoriamente `release.version`, `git.commit.sha` e `manifest.digest`. |
| **Envelope Transacional com Contexto** | Estrutura de persistência (`OutboxRecord`) e transporte (`ServiceBusReceivedMessage`) que encapsula a carga financeira e preserva os metadados W3C. | Impede a criação de traces órfãos entre a API e os Workers assíncronos. |
| **Higienização de Telemetria** | Processo mandatório que proíbe e remove qualquer Informação Pessoal Identificável (PII) ou dado financeiro protegido (CPF, e-mail, agência, conta, payload bruto) dos traces e logs. | Apenas IDs técnicos opacos (`PedidoId`, `EndToEndId`, `CorrelationId`) são permitidos. |
| **Davis AI Engine** | Motor de inteligência causal do Dynatrace que correlaciona bilhões de dependências para identificar a causa raiz determinística de incidentes sem falsos positivos. | Gera os diagnósticos que fundamentam as decisões de rollback da equipe de engenharia. |
| **Error Budget (Orçamento de Erro)** | Margem permitida de degradação ou indisponibilidade calculada a partir do SLO ($100\% - \text{SLO}$), cujo esgotamento impede novas liberações. | Para as APIs Pix, o SLO é fixado em $\ge 99.99\%$ de disponibilidade e P95 $< 400\text{ ms}$. |
| **Laudo do Guardian** | Registro auditável gerado pelo SRG contendo a pontuação de saúde, métricas violadas e veredito final (`PASS` ou `FAIL`) associado ao release. | Armazenado no GitHub Step Summary para auditoria regulatória da B3/BACEN. |
