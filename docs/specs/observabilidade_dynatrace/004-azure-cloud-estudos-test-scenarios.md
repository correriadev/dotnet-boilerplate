# 004 — Test Scenarios Specification: Observabilidade Dynatrace & Quality Gates
**Domínio:** `observabilidade_dynatrace`  
**Projeto:** `azure-cloud-estudos`  
**Data:** 2026-10-07  
**Status:** Aprovado  
**Referências:** [001-problem-space.md](./001-problem-space.md), [002-context-map.md](./002-context-map.md), [003-azure-cloud-estudos-tactical-design.md](./003-azure-cloud-estudos-tactical-design.md)

Especificação formal de cenários de teste derivados do Tactical Design. Somente especificações, formato Given-When-Then, utilizando a Linguagem Ubíqua. Sem código de implementação.

---

## 1. Unit Tests

### 1.1 Aggregates e Aggregate Roots

#### `LaudoGuardian`
* **TS-OBS-01: Should emit Pass verdict when all Golden Signals and Pix SLOs meet thresholds**
  * Given métricas de release com Latência P95 = 280ms (limiar: 400ms), Taxa de Erro = 0.02% (limiar: 0.10%) e Latência SPI Bacen = 850ms (limiar: 2000ms)
  * When a avaliação do `LaudoGuardian` é processada
  * Then o veredito emitido é `Pass`, status é `Concluido` e `LaudoGuardianEmitido` é disparado
* **TS-OBS-02: Should emit Fail verdict when P95 latency exceeds the 400ms threshold**
  * Given métricas de release com Latência P95 = 480ms e Taxa de Erro = 0.01%
  * When a avaliação do `LaudoGuardian` é processada
  * Then o veredito emitido é `Fail`, registrando violação crítica de latência
* **TS-OBS-03: Should emit Fail verdict when BACEN SPI liquidation latency exceeds 2 seconds**
  * Given métricas de release com Latência SPI Bacen = 2300ms
  * When a avaliação do `LaudoGuardian` é processada
  * Then o veredito emitido é `Fail` com violação de SLA regulatório
* **TS-OBS-04: Should reject any modification once the Guardian report is sealed**
  * Given um `LaudoGuardian` com status `Concluido`
  * When uma tentativa de alterar o veredito ou as violações for executada
  * Then a operação é rejeitada garantindo a imutabilidade absoluta do laudo

#### `InspecaoBakePeriod`
* **TS-OBS-05: Should complete bake inspection successfully when no Davis AI anomalies occur during 10 minutes**
  * Given uma nova revisão ACA em Produção e tempo decorrido de 10 minutos sem problemas detectados
  * When a inspeção de bake é finalizada
  * Then o status transiciona para `Estavel` autorizando o encerramento da revisão anterior
* **TS-OBS-06: Should transition to Degraded and trigger P1 alert when Davis AI detects critical anomaly during bake window**
  * Given uma revisão ACA sob observação na janela de 10 minutos
  * When a Davis AI sinaliza um problema crítico de degradação de dependência (`DavisProblemDetected`)
  * Then a inspeção transiciona imediatamente para `Degradado` e o evento `AlertaP1BakePeriodDegradado` é emitido com a URL do `rollback.yml`

---

### 1.2 Value Objects

#### `ContextoW3C`
* **TS-OBS-07: Should create ContextoW3C successfully when TraceParent follows valid 55-character format**
  * Given a string de cabeçalho `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01`
  * When a criação de `ContextoW3C` é solicitada
  * Then a instância é criada como objeto de valor imutável com TraceId e SpanId normalizados
* **TS-OBS-08: Should reject creation when TraceParent format or length is invalid**
  * Given a string `invalid-traceparent-header`
  * When a instanciação de `ContextoW3C` é tentada
  * Then uma exceção de validação de domínio é lançada impedindo a propagação de formato corrompido

#### `MetricasGoldenSignals`
* **TS-OBS-09: Should validate compliance with BACEN SLA when P99 is equal to or below 2000ms**
  * Given `MetricasGoldenSignals` com P99 = 1950ms
  * When `AtendeSlaBacen()` é invocado
  * Then retorna verdadeiro (`true`)
* **TS-OBS-10: Should reject negative latency or error rate values out of bounds**
  * Given taxa de erro informada como -5% ou 105%
  * When `MetricasGoldenSignals` é instanciado
  * Then a validação de invariante falha com erro de domínio

#### `IdentificadorTecnico`
* **TS-OBS-11: Should accept valid PedidoId GUID and 32-character EndToEndId**
  * Given um `PedidoId` válido e um `EndToEndId` padrão BACEN com exatamente 32 caracteres alfanuméricos
  * When `IdentificadorTecnico.Validar()` é chamado
  * Then o objeto de valor imutável é retornado com sucesso
* **TS-OBS-12: Should reject strings containing personal identifiable patterns**
  * Given uma tentativa de passar CPF no lugar de `EndToEndId`
  * When `IdentificadorTecnico.Validar()` é chamado
  * Then a criação é rejeitada por violação de regra de sanitização

---

### 1.3 Domain Services e Políticas

#### `SanitizadorTelemetriaPolicy`
* **TS-OBS-13: Should remove sensitive PII keys and preserve technical correlation keys**
  * Given tags de telemetria contendo `pedido_id`, `cpf`, `email` e `chave_pix`
  * When `SanitizadorTelemetriaPolicy.AplicarTagsSeguras()` é executado
  * Then apenas `pedido_id` é mantido na `Activity` e todos os campos PII são expurgados
* **TS-OBS-14: Should detect and block sensitive account number patterns in structured log arguments**
  * Given um argumento de log com chave `conta_corrente` ou formato de agência bancária
  * When avaliado pela política de sanitização
  * Then a entrada é classificada como sensível e bloqueada de envio para a telemetria

---

### 1.4 Domain Events

#### `LaudoGuardianEmitido` e `AlertaP1BakePeriodDegradado`
* **TS-OBS-15: Should generate immutable event with evaluation ID, release metadata and timestamp**
  * Given a conclusão de um laudo pelo SRG
  * When `LaudoGuardianEmitido` é emitido
  * Then o payload contém `EvaluationId`, `ReleaseVersion`, `CommitSha`, `ManifestDigest` e timestamp UTC imutável
* **TS-OBS-16: Should contain direct rollback action link in P1 bake alert event**
  * Given a detecção de anomalia no bake period
  * When `AlertaP1BakePeriodDegradado` é emitido
  * Then o payload inclui o ID do problema no Dynatrace e a URL canônica para o workflow `rollback.yml`

---

## 2. Integration Tests

### 2.1 Repositórios e Persistência do Outbox

#### `IOutboxTraceRepository` (Azure SQL Server 2022)
* **TS-OBS-17: Should persist and retrieve transaction record with W3C TraceParent and TraceState intact**
  * Given uma transação financeira em processamento com `Activity.Current` ativo
  * When o `OutboxRecord` é persistido na tabela `[pix].[OutboxRecords]` via Dapper
  * Then as colunas `TraceParent` e `TraceState` são armazenadas com sucesso e recuperadas identicamente na leitura
* **TS-OBS-18: Should allow nullable TraceParent for legacy or migration records without failing queries**
  * Given registros legados de Outbox com colunas de rastreamento nulas
  * When a consulta em lote (`TOP (@batchSize) WITH (UPDLOCK, READPAST)`) é executada
  * Then os registros são retornados sem erro de mapeamento e sem quebra de compatibilidade reversa

---

### 2.2 Casos de Uso e Propagação de Contexto

#### Propagação Assíncrona no Azure Service Bus
* **TS-OBS-19: Should inject W3C metadata into Service Bus message properties and restore context in Workers**
  * Given um registro de Outbox contextualizado publicado pelo `OutboxRelay` no Azure Service Bus
  * When a mensagem é recebida pelo `Outbank.Connector`
  * Then as `ApplicationProperties` contêm `traceparent`, o worker executa `SetParentId()` e o `TraceId` da operação de liquidação é idêntico ao da API HTTP de origem

#### `AvaliarGuardianHomologacaoUseCase`
* **TS-OBS-20: Should fail GitHub Actions job when Dynatrace SRG evaluation returns Fail verdict**
  * Given a suíte de testes de carga em Homologação concluída
  * When o caso de uso consulta a avaliação do SRG e recebe `Fail`
  * Then o caso de uso dispara `QualityGateHomologacaoReprovado`, persiste o sumário de falhas e encerra o processo com código de erro não-zero

---

### 2.3 Resiliência e Integrações Externas

#### `BatchActivityExportProcessor` (Fail-Open OTLP)
* **TS-OBS-21: Should silently drop excess telemetry without raising exceptions when Dynatrace SaaS is unreachable**
  * Given o endpoint OTLP do Dynatrace configurado para um endereço simulado com timeout ou indisponível
  * When 500 requisições financeiras são submetidas à `Pix.CashOut.Api` em alta frequência
  * Then todas as transações HTTP concluem com sucesso (200 OK), o buffer em memória descarta os spans excedentes e zero exceções vazam para os clientes

#### `DynatraceGuardianClient` (Polly v8 Resilience)
* **TS-OBS-22: Should retry transient HTTP 503/429 errors from Dynatrace API with backoff before failing**
  * Given a API do Dynatrace retornando HTTP 429 (Too Many Requests) momentâneo
  * When a esteira consulta o status da avaliação do Guardian
  * Then a política de resiliência executa até 3 retentativas com jitter exponencial antes de consolidar o resultado

---

## 3. Functional Tests

### 3.1 Happy Path Flows

#### Fluxo 1 — Rastreabilidade Contínua Ponta a Ponta Sem Spans Órfãos
* **TS-OBS-23: Should trace transaction continuously from HTTP API through Outbox and Service Bus to Worker**
  * Given um cliente autenticado submetendo requisição Pix CashOut com cabeçalho `traceparent`
  * When a requisição é aceita pela API, persistida no Outbox SQL, despachada para o Service Bus e consumida pelo `Outbank.Connector`
  * Then no Dynatrace SaaS o *PurePath* correlaciona os spans da API, query Dapper no Azure SQL, publicação no broker e execução no worker sob o mesmo `TraceId` global sem spans desconectados

#### Fluxo 2 — Homologação Aprovada no Quality Gate Liberando Gate Manual
* **TS-OBS-24: Should approve Homologation quality gate and unlock Production manual approval gate**
  * Given a esteira `promote-hml.yml` executando testes de carga sob o manifesto imutável de release
  * When o evento SDLC é enviado ao Dynatrace e o SRG retorna `Pass` com 100% de conformidade de SLOs
  * Then o laudo é publicado no GitHub Step Summary, o job de HML é concluído com sucesso e o gate de aprovação manual para Produção é habilitado

#### Fluxo 3 — Janela de Bake Estável em Produção
* **TS-OBS-25: Should complete 10-minute bake period in Production and complete release rollout**
  * Given o deploy Blue/Green concluído em Produção na esteira `promote-prod.yml`
  * When a revisão passa pelos 10 minutos de tráfego real sem alertas de anomalia do Davis AI
  * Then a esteira consolida a versão como estável e desativa a revisão anterior com segurança

---

### 3.2 Error & Boundary Flows

#### Fluxo 4 — Bloqueio Mandatório de Promoção por Regressão no Quality Gate
* **TS-OBS-26: Should block Production manual gate when Homologation SRG detects latency regression**
  * Given testes de carga em HML onde uma regressão na query Dapper eleva a latência P95 para 650ms
  * When o Dynatrace SRG avalia a janela de teste e emite veredito `Fail`
  * Then a esteira `promote-hml.yml` falha imediatamente o job, anexa as métricas violadas no Step Summary e **não libera** a solicitação de aprovação manual para Produção

#### Fluxo 5 — Degradação no Bake Period Acionando Alerta P1 e Rollback
* **TS-OBS-27: Should trigger immediate P1 alert with rollback link when Davis AI detects errors during bake window**
  * Given a nova revisão ACA operando durante a janela de bake de 10 minutos em Produção
  * When a taxa de erro HTTP 5xx sobe para 1.5% disparando detecção de anomalia pela Davis AI
  * Then a esteira captura o incidente, emite alerta P1 imediato no canal SRE (Teams/PagerDuty) com evidência de causa raiz e link direto para o workflow `rollback.yml`
