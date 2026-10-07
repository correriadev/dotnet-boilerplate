# REWORK LOG — Feature F001 (observabilidade_dynatrace)

## Rework #1 — 2026-10-07

### Findings: The Grumpy Tech Lead (Score: 0.55 / Threshold: 0.70)
1. **Falta de Propagação Real nos Adapters:** Como o Dynatrace PurePath manterá a correlação ponta a ponta se o AcceptanceStore e ExecutionStore não persistem TraceParent e TraceState no Outbox, o AzureServiceBusPublisher omite os cabeçalhos W3C nas ApplicationProperties e os workers nunca restauram a Activity via SetParentId?
2. **Desacoplamento do Sanitizador de PII dos Pipelines Reais:** Como a conformidade com a LGPD e o sigilo bancário são garantidos se o SanitizadorTelemetriaPolicy não está acoplado ao middleware HTTP nem ao pipeline de mensageria, permitindo que tags com dados sensíveis (PII) sejam exportadas sem filtragem para a telemetria externa?
3. **Quality Gates Simulados na Esteira:** Qual é a efetividade dos quality gates de Homologação e Produção quando os scripts das esteiras utilizam vereditos e métricas hardcoded sem consultar a API do Dynatrace SRG ou os problemas do Davis AI, permitindo que artefatos com regressões severas sejam promovidos silenciosamente?

### Findings: Adversarial QA (Score: 0.35 / Threshold: 0.70 / hasHighCriticalVuln: TRUE)
#### Vulnerabilidades Críticas/Altas:
1. **[HIGH] DATA_EXPOSURE em SanitizadorTelemetriaPolicy:**
   - Evidência: `SanitizadorTelemetriaPolicy.cs`. Chaves como `pix_key`, `chave` e contas bancárias não são detectadas; `ValorContemPii` não possui regex de telefone; e `AplicarTagsSeguras` filtra apenas string tags de `activity.Tags`, ignorando tags boxed/numéricas em `activity.TagObjects`.
2. **[HIGH] AUTH_BYPASS no Gate de Homologação:**
   - Evidência: `.github/workflows/promote-hml.yml:95-112`. O step do Dynatrace SRG define `$laudoStatus = "PASS"` fixo sem invocar a API real (`DYNATRACE_URL`), permitindo que builds degradados passem sem validação.

#### Edge Cases Perdidos:
1. **[TIER 2 | -0.15] Quebra de Rastreabilidade Assíncrona:** `OutboxRepository.cs` não inclui `TraceParent` e `TraceState` no SQL de INSERT; `AzureServiceBusPublisher` não anexa `traceparent` no `ServiceBusMessage.ApplicationProperties`; `PaymentExecutionWorker.cs` não restaura `Activity.SetParentId`.
2. **[TIER 3 | -0.05] Métricas Dynatrace em Inglês no LaudoGuardian:** `LaudoGuardian.cs` só busca termos em português (`Latencia`, `Erro`, `SPI`), rebaixando violações legítimas em inglês (`response_time_p95`, `failure_rate`) para `Warning` em vez de `Fail`. Nulo em `NomeMetrica` lança `NullReferenceException`.
3. **[TIER 3 | -0.05] IdentificadorTecnico e Validação Estrutural BACEN:** `IdentificadorTecnico.cs` valida regex alfanumérico antes de PII, não detecta CPF sem formatação preenchido com zeros, e não valida a estrutura oficial do EndToEndId BACEN (prefixo 'E'/'M', ISPB e timestamp).
4. **[TIER 3 | -0.05] Percentis Invertidos e NaN em MetricasGoldenSignals:** Não valida `double.IsNaN`/`IsInfinity` nem garante `LatenciaP99Ms >= LatenciaP95Ms`.
5. **[TIER 3 | -0.05] All-Zero W3C TraceParent e Null em ContextoW3C:** Permite `00-00000000000000000000000000000000-0000000000000000-01` (violando a especificação W3C seção 3.2.2.3) e leitura de propriedades em struct `default` lança exceção.
