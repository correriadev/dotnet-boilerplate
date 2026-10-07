# 003 — Tactical Design: Solution Space
**Domínio:** `observabilidade_dynatrace`  
**Projeto:** `azure-cloud-estudos`  
**Data:** 2026-10-07  
**Status:** Aprovado  
**Referências:** [001-problem-space.md](./001-problem-space.md), [002-context-map.md](./002-context-map.md), [pbb_observabilidade_dynatrace.md](../../feature/pbb_observabilidade_dynatrace.md)

---

## 0. Refinement Questions and Answers

| ID | Category | Question | Recommendation | Final Answer | Answered By |
|---|---|---|---|---|---|
| Q01 | `integration` | Como o contexto de rastreabilidade distribuída (W3C Traceparent) deve ser propagado através da camada assíncrona do Outbox (Azure SQL) e das mensagens do Azure Service Bus para que os workers mantenham o trace ponta a ponta contínuo no Dynatrace? | Persistir `traceparent` e `tracestate` em colunas dedicadas na tabela `[pix].[OutboxRecords]`, repassá-los nas `ApplicationProperties` do Azure Service Bus e restaurá-los via `SetParentId()` nos workers. | Persistir `traceparent` e `tracestate` em colunas dedicadas na tabela `[pix].[OutboxRecords]`, repassá-los nas `ApplicationProperties` do Azure Service Bus e restaurá-los via `SetParentId()` nos workers. | human |
| Q02 | `failure` | Qual deve ser a política de tolerância a falhas e sobrecarga do pipeline de telemetria OTLP (.NET 8) caso o endpoint do Dynatrace SaaS fique inacessível ou sofra lentidão na rede? | Fail-Open não-bloqueante com `BatchExportProcessor` assíncrono em memória, buffer limitado e descarte seguro em overflow sem lançar exceções nem degradar transações financeiras. | Fail-Open não-bloqueante com `BatchExportProcessor` assíncrono em memória, buffer limitado e descarte seguro em overflow sem lançar exceções nem degradar transações financeiras. | human |
| Q03 | `acceptance` | Como o resultado da avaliação do Dynatrace Site Reliability Guardian (SRG) deve ser correlacionado com o commit SHA e o manifesto da esteira no GitHub Actions para auditoria e rastreabilidade? | Vínculo de metadados de release no evento SDLC (`release.version`, `git.commit.sha`, `manifest.digest`, `deployment.environment`), persistindo o laudo no GitHub Step Summary e artefatos de auditoria. | Vínculo de metadados de release no evento SDLC (`release.version`, `git.commit.sha`, `manifest.digest`, `deployment.environment`), persistindo o laudo no GitHub Step Summary e artefatos de auditoria. | human |

---

## 1. Main Structure

| Element | Layer / Type | Invariants / Tech Rules | 4-line Snippet |
|---|---|---|---|
| `LaudoGuardian` | Domain / Aggregate Root | Vinculado a `ReleaseManifest`; emite veredito imutável (`Pass`, `Fail`, `Warning`); violação de latência ou erro impõe status `Fail`. | ```csharp<br>public sealed class LaudoGuardian : AggregateRoot {<br>    public ReleaseMetadata Release { get; private set; }<br>    public VereditoQualityGate Veredito { get; private set; }<br>    public void ConcluirAvaliacao(IReadOnlyList<ViolacaoSLO> violacoes) => ...;<br>}<br>``` |
| `EnvelopeTransacional` | Infrastructure / Outbox Entity | Encapsula comando financeiro e metadados `ContextoW3C`; imutável pós-criação; transição atômica para `PUBLISHED` via CAS lease. | ```csharp<br>public sealed class OutboxRecordContextualizado {<br>    public Guid EventId { get; init; }<br>    public ContextoW3C TraceContext { get; init; }<br>    public string Payload { get; init; } // Zero PII estrito<br>}<br>``` |
| `InspecaoBakePeriod` | Domain / Aggregate Root | Vigia nova revisão ACA por 10 minutos; anomalia Davis AI dispara evento `AlertaP1BakePeriodDegradado` com link de rollback. | ```csharp<br>public sealed class InspecaoBakePeriod : AggregateRoot {<br>    public string RevisionName { get; init; }<br>    public TimeSpan JanelaRestante => ...;<br>    public void RegistrarAnomaliaDavis(string problemId, string rca) => ...;<br>}<br>``` |
| `SanitizadorTelemetria` | Domain / Policy Service | Intercepta tags de telemetria; proíbe e purga CPF, e-mail, telefone e dados de conta; aprova apenas IDs técnicos opacos. | ```csharp<br>public sealed class SanitizadorTelemetriaPolicy {<br>    public static bool ContemDadosSensiveis(string tagKey) => ...;<br>    public static void AplicarTagsSeguras(Activity activity, IdentificadorTecnico ids) => ...;<br>}<br>``` |

---

## 2. Value Objects / Types / Interfaces

| Name | Context / Layer | Validation & Typing Rules | 4-line Snippet |
|---|---|---|---|
| `ContextoW3C` | Shared Kernel / Value Object | `TraceParent` deve ter 55 caracteres (`00-{traceId}-{spanId}-{flags}`); imutável; `TraceState` opcional normalizado. | ```csharp<br>public readonly record struct ContextoW3C(string TraceParent, string? TraceState) {<br>    public static ContextoW3C Create(string parent, string? state) =><br>        parent?.Length == 55 ? new(parent, state) : throw new ArgumentException("TraceParent W3C inválido");<br>}<br>``` |
| `MetricasGoldenSignals` | Domain / Value Object | Latências $\ge 0\text{ ms}$; taxa de erro entre $0.0$ e $100.0\%$; vazão TPS $\ge 0$; imutável. | ```csharp<br>public readonly record struct MetricasGoldenSignals(<br>    double LatenciaP95Ms, double LatenciaP99Ms, double TaxaErroPercent, double VazaoTps) {<br>    public bool AtendeSlaBacen(double tetoMs = 2000.0) => LatenciaP99Ms <= tetoMs;<br>}<br>``` |
| `IdentificadorTecnico` | Shared Kernel / Value Object | Wrapper para `PedidoId` (Guid) e `EndToEndId` (32 alfanuméricos BACEN); rejeita strings de CPF ou nomes. | ```csharp<br>public readonly record struct IdentificadorTecnico(Guid PedidoId, string EndToEndId) {<br>    public static IdentificadorTecnico Validar(Guid id, string e2e) =><br>        e2e?.Length == 32 ? new(id, e2e) : throw new ArgumentException("EndToEndId BACEN inválido");<br>}<br>``` |
| `VereditoQualityGate` | Domain / Value Object (Enum) | Estados finitos: `Pass` (aprovado), `Fail` (bloqueante), `Warning` (alerta), `Inconclusive` (dados insuficientes). | ```csharp<br>public enum VereditoQualityGate {<br>    Pass = 1, Fail = 2, Warning = 3, Inconclusive = 4<br>}<br>``` |

---

## 3. Domain Services / Use Cases / Actions

| Operation / Use Case | Responsabilidade | Coordena / Subscrições | 4-line Snippet |
|---|---|---|---|
| `AvaliarGuardianHomologacaoUseCase` | Executa avaliação do SRG no GitHub Actions; processa violações de SLO; emite veredito de bloqueio. | `IDynatraceGuardianClient`, `ILaudoGuardianRepository` | ```csharp<br>public async Task<VereditoQualityGate> ExecutarAsync(EventoSDLC evento, CancellationToken ct) {<br>    var laudo = await _guardianClient.AvaliarReleaseAsync(evento, ct);<br>    await _repository.SalvarLaudoAsync(laudo, ct); return laudo.Veredito;<br>}<br>``` |
| `PropagarContextoOutboxService` | Captura `Activity.Current` ativo e anexa `ContextoW3C` no `OutboxRecord` antes do commit ACID. | `ActivitySource`, `IOutboxRepository` | ```csharp<br>public static void EnriquecerRegistroOutbox(OutboxRecord record, Activity? current) {<br>    if (current != null) record.SetTraceContext(current.Id!, current.TraceStateString);<br>}<br>``` |
| `VigiarBakePeriodProducaoUseCase` | Monitora a janela de 10 min pós-deploy Blue/Green; se Davis AI acusar problema crítico, emite alerta P1 para rollback. | `IDavisProblemClient`, `IAlertaPlantaoSRE` | ```csharp<br>public async Task VigiarJanelaAsync(string revisionName, TimeSpan janela, CancellationToken ct) {<br>    var anomalias = await _davisClient.ConsultarProblemasRevisaoAsync(revisionName, ct);<br>    if (anomalias.Any()) await _alerter.DispararAlertaP1RollbackAsync(revisionName, anomalias, ct);<br>}<br>``` |

---

## 4. Events / Messages / Async Flows

| Event / Action Name | Trigger | Minimum Payload | Consumers |
|---|---|---|---|
| `EventoSDLCDeCargaDisparado` | Fim dos testes de carga na esteira `promote-hml.yml` | `ReleaseVersion`, `CommitSha`, `ManifestDigest`, `Environment`, `Timestamp` | Dynatrace SRG Engine |
| `LaudoGuardianEmitido` | Conclusão da avaliação de SLOs pelo Dynatrace SRG | `EvaluationId`, `ReleaseVersion`, `Veredito`, `Violacoes`, `Timestamp` | GitHub Actions Step Summary, SRE Portal |
| `QualityGateHomologacaoReprovado` | Veredito `Fail` no laudo do Guardian | `ReleaseVersion`, `EvaluationId`, `MotivoFalha`, `MetricasVioladas` | GitHub Actions (falha o job, bloqueia gate de PROD) |
| `AlertaP1BakePeriodDegradado` | Davis AI detecta anomalia durante os 10 min de bake | `RevisionName`, `DavisProblemId`, `CausaRaiz`, `RollbackUrl`, `Timestamp` | Equipe SRE On-Call (Teams / PagerDuty) |
| `MensagemServiceBusContextualizada` | Despacho de mensagem pelo `OutboxRelay` | `TraceParent`, `TraceState`, `SessionId`, `Payload` financeiro | Workers (`Outbank.Connector`, `Webhook.Dispatcher`) |

---

## 5. Persistence / Repository / Data Access Interfaces

| Resource / Adapter | Methods / Actions | Return Types / Expected State |
|---|---|---|
| `IOutboxTraceRepository` | `PersistirComContextoAsync(record, ct)`<br>`ObterPendentesComContextoAsync(batchSize, ct)` | `Task`<br>`Task<IReadOnlyList<OutboxRecordContextualizado>>` |
| `IDynatraceGuardianClient` | `DispararAvaliacaoAsync(evento, ct)`<br>`ConsultarLaudoAsync(evaluationId, ct)` | `Task<string>` (evaluationId)<br>`Task<LaudoGuardian>` |
| `IDavisProblemClient` | `ConsultarProblemasRevisaoAsync(revision, janela, ct)` | `Task<IReadOnlyList<DavisProblemDTO>>` |
| `IAlertaPlantaoSRE` | `DispararAlertaP1RollbackAsync(revision, problemas, ct)` | `Task` |

```csharp
// Exemplo de assinatura de Porta de Integração SRG (max 4 linhas):
public interface IDynatraceGuardianClient {
    Task<string> IniciarAvaliacaoAsync(EventoSDLC evento, CancellationToken ct);
    Task<LaudoGuardian> ObterResultadoAvaliacaoAsync(string evaluationId, CancellationToken ct);
}
```

---

## 6. Ordered Development Tasks

```json
[
  {
    "id": "01",
    "title": "Implementar Value Objects de Telemetria e Contexto W3C",
    "description": "Criar tipos imutáveis para representação e validação formal de TraceParent, TraceState e Golden Signals.",
    "scope": [
      "src/Pix.Processing.Domain/Telemetry/ContextoW3C.cs",
      "src/Pix.Processing.Domain/Telemetry/MetricasGoldenSignals.cs",
      "src/Pix.Processing.Domain/Telemetry/VereditoQualityGate.cs"
    ],
    "acceptance": [
      "TraceParent rejeita formato fora da especificação W3C de 55 caracteres",
      "MetricasGoldenSignals calcula conformidade com SLA Bacen de 2s",
      "Tipos são imutáveis e testados unitariamente sem dependência de I/O"
    ],
    "depends_on": null
  },
  {
    "id": "02",
    "title": "Implementar Políticas de Sanitização e Mascaramento de PII",
    "description": "Assegurar que nenhuma chave Pix, CPF ou dado de conta bancária seja inserido em tags de telemetria ou logs estruturados.",
    "scope": [
      "src/Pix.Processing.Domain/Telemetry/SanitizadorTelemetriaPolicy.cs",
      "src/Pix.Processing.Domain/Telemetry/IdentificadorTecnico.cs"
    ],
    "acceptance": [
      "IdentificadorTecnico aceita apenas PedidoId e EndToEndId válidos",
      "SanitizadorTelemetriaPolicy remove e bloqueia atributos com padrões de CPF ou e-mail",
      "Cobertura unitária de 100% nas regras de mascaramento na origem"
    ],
    "depends_on": "01"
  },
  {
    "id": "03",
    "title": "Evoluir Schema do Outbox com Colunas de Contexto W3C",
    "description": "Adicionar colunas TraceParent e TraceState no [pix].[OutboxRecords] e adaptar adapter de escrita Dapper.",
    "scope": [
      "src/Pix.Migrator/Scripts/008_add_w3c_trace_context_outbox.sql",
      "src/Pix.Processing.Infrastructure/Adapters/OutboxAndAuthAdapters.cs"
    ],
    "acceptance": [
      "Script DbUp expande tabela sem indisponibilidade (nullable com default)",
      "PersistirTransacao insere TraceParent e TraceState da Activity corrente",
      "Consulta Dapper recupera metadados W3C sem quebrar leituras existentes"
    ],
    "depends_on": "02"
  },
  {
    "id": "04",
    "title": "Propagar Contexto W3C no Service Bus e Restaurar nos Workers",
    "description": "Injetar metadados W3C nas ApplicationProperties das mensagens enviadas pelo OutboxRelay e restaurar contexto via SetParentId nos workers.",
    "scope": [
      "src/Pix.Processing.Infrastructure/Adapters/OutboxAndAuthAdapters.cs",
      "src/Outbank.Connector/Program.cs",
      "src/Webhook.Dispatcher/Program.cs"
    ],
    "acceptance": [
      "Mensagens enviadas contêm chaves traceparent e tracestate no header da mensagem",
      "Workers inicializam Activity com SetParentId restaurando o trace original",
      "Teste de integração confirma rastreabilidade contínua da API ao Worker sem spans órfãos"
    ],
    "depends_on": "03"
  },
  {
    "id": "05",
    "title": "Configurar Exportador OTLP Fail-Open com Buffer Bounded no .NET 8",
    "description": "Configurar o OpenTelemetry SDK nas APIs e Workers para despacho assíncrono via OTLP com descarte seguro em overflow de buffer.",
    "scope": [
      "src/Pix.CashOut.Api/Program.cs",
      "src/Pix.Payment.Api/Program.cs",
      "src/Pix.Processing.Infrastructure/Telemetry/OtlpTelemetryConfiguration.cs"
    ],
    "acceptance": [
      "BatchActivityExportProcessor configurado com buffer limitado em memória",
      "Simulação de indisponibilidade do Dynatrace não gera exceções nem eleva latência das APIs",
      "Descarte silencioso verificado sob sobrecarga de spans"
    ],
    "depends_on": "04"
  },
  {
    "id": "06",
    "title": "Implementar Adaptador ACL do Dynatrace Site Reliability Guardian",
    "description": "Criar cliente HTTP com Polly v8 para integração com a API do Dynatrace SRG e Davis AI Problems.",
    "scope": [
      "src/Pix.Processing.Infrastructure/Telemetry/DynatraceGuardianClient.cs",
      "src/Pix.Processing.Domain/Telemetry/LaudoGuardian.cs"
    ],
    "acceptance": [
      "Cliente formata evento SDLC com release.version e git.commit.sha",
      "Resposta do SRG traduzida confiavelmente para LaudoGuardian e VereditoQualityGate",
      "Timeout estrito de 3 minutos e retries com backoff implementados via Polly"
    ],
    "depends_on": "05"
  },
  {
    "id": "07",
    "title": "Integrar Quality Gate do Dynatrace SRG na Esteira de Homologação",
    "description": "Atualizar promote-hml.yml para disparar avaliação do SRG pós-testes de carga e bloquear gate manual caso o laudo seja FAIL.",
    "scope": [
      ".github/workflows/promote-hml.yml",
      ".github/actions/dynatrace-srg-gate/action.yml"
    ],
    "acceptance": [
      "Workflow dispara evento SDLC ao término dos testes de carga de HML",
      "Job falha automaticamente e bloqueia gate de PROD em caso de veredito FAIL",
      "Resultado detalhado é publicado no GitHub Step Summary"
    ],
    "depends_on": "06"
  },
  {
    "id": "08",
    "title": "Integrar Monitoramento do Bake Period e Alerta P1 na Esteira de Produção",
    "description": "Atualizar promote-prod.yml para observar revisão por 10 minutos via Dynatrace e notificar SRE On-Call com link de rollback em caso de anomalia.",
    "scope": [
      ".github/workflows/promote-prod.yml",
      ".github/workflows/rollback.yml"
    ],
    "acceptance": [
      "Janela de 10 min pós-deploy Blue/Green consulta problemas do Davis AI para a revisão",
      "Anomalia detectada emite alerta P1 imediato no canal SRE com link do rollback.yml",
      "Deploy é consolidado apenas após 10 minutos sem incidentes críticos"
    ],
    "depends_on": "07"
  }
]
```
