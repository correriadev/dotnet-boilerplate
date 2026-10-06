---
doc_type: feature
domain: plataforma_pix
stack: [.NET 8, Azure SQL Server, Dapper, DbUp, Redis, Azure Service Bus, Polly v8]
node_id: "feature:plataforma-pix"
tags: [pix, cashout, billing, outbox, idempotency, dapper, dbup, spi-liquidacao]
edges:
  - relation: implements
    target: "adr:architecture"
    read: must
  - relation: tested_by
    target: "adr:tests"
    read: must
updated: 2026-10-05
---
```graph
{"node_id":"feature:plataforma-pix","domain":"plataforma_pix","implements":["adr:architecture"],"tested_by":["adr:tests"],"entrypoints":["src/Pix.CashOut.Api/Program.cs","src/Pix.Payment.Api/Program.cs","src/Outbank.Connector/Program.cs","src/Pix.Reconciliation.Worker/Program.cs","src/Webhook.Dispatcher/Program.cs"],"registration_files":["src/Pix.CashOut.Api/Controllers/CashOutController.cs","src/Pix.Payment.Api/Controllers/BillingController.cs","src/Pix.Processing.Infrastructure/DbUpRunner.cs","src/Pix.Processing.Infrastructure/PixDbContext.cs","src/Pix.Processing.Application/Ports/Ports.cs"],"reference_files":["src/Pix.Processing.Application/Features/AcceptRequest/AcceptRequestUseCase.cs","src/Pix.Processing.Application/Features/ExecutePayment/ExecutePaymentUseCase.cs","src/Pix.Processing.Domain/Entities/PedidoPix.cs"],"code_files":["src/Pix.Processing.Infrastructure/Repositories/PedidoPixRepository.cs","src/Pix.Processing.Infrastructure/Repositories/OutboxRepository.cs","src/Pix.Processing.Infrastructure/Repositories/ReadOnlyReplicaRepository.cs","src/Pix.Processing.Infrastructure/Adapters/AcceptanceStore.cs","src/Pix.Processing.Infrastructure/Adapters/ExecutionStore.cs","src/Pix.Processing.Infrastructure/Adapters/OutboxAndAuthAdapters.cs","src/Pix.Processing.Infrastructure/Adapters/RedisAndAclAdapters.cs","src/Pix.Processing.Infrastructure/Adapters/ResilientCacheAdapter.cs"],"test_files":["tests/Unit/DomainTests.cs","tests/Unit/DomainPixTests.cs","tests/Integration/PersistenceTests.cs","tests/Integration/DapperRepositoryTests.cs"],"knowledge":{"schema_version":1,"entities":[{"id":"capability:accept-pix-request","type":"capability","label":"Accept Pix","definition":"Accept Pix idempotently.","aliases":[]},{"id":"capability:execute-pix-payment","type":"capability","label":"Execute Pix","definition":"Execute Pix with CAS fencing.","aliases":[]},{"id":"rule:positive-money-amount","type":"rule","label":"Positive amount","definition":"Amount > 0.","aliases":[]},{"id":"rule:idempotent-fingerprint-match","type":"rule","label":"Fingerprint match","definition":"Matching fingerprint required.","aliases":[]},{"id":"rule:terminal-state-precedence","type":"rule","label":"Terminal state precedence","definition":"Terminal state before intent.","aliases":[]},{"id":"rule:expired-lease-crash-triage","type":"rule","label":"Crash triage","definition":"Crash triage to ResultadoIncerto.","aliases":[]},{"id":"rule:outbox-cas-lease","type":"rule","label":"Outbox CAS lease","definition":"Lease match for PUBLISHED.","aliases":[]},{"id":"rule:anti-ssrf-ipv6-mapped","type":"rule","label":"Anti-SSRF IPv6 mapped","definition":"Deny IPv4-mapped IPv6.","aliases":[]},{"id":"contract:accept-endpoint","type":"contract","label":"Accept contract","definition":"Return 202 with RequestCode.","aliases":[]},{"id":"decision:outbox-pattern","type":"decision","label":"Outbox pattern","definition":"Atomic transactional outbox.","aliases":[]}],"claims":[{"id":"claim:amount-positive-guard","subject":"capability:accept-pix-request","relation":"constrained_by","object":"rule:positive-money-amount","statement":"Money requires amount > 0.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Domain/ValueObjects/Money.cs","locator":"Money guard","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:idempotency-guard","subject":"capability:accept-pix-request","relation":"constrained_by","object":"rule:idempotent-fingerprint-match","statement":"Store checks fingerprint on replay.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Adapters/AcceptanceStore.cs","locator":"ConfirmarAceiteAsync","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:terminal-state-precedence-guard","subject":"capability:execute-pix-payment","relation":"constrained_by","object":"rule:terminal-state-precedence","statement":"ExecutionStore checks terminal status returning JaConcluido.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Adapters/ExecutionStore.cs","locator":"AdquirirExecucaoAsync","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:crash-triage-guard","subject":"capability:execute-pix-payment","relation":"constrained_by","object":"rule:expired-lease-crash-triage","statement":"Crashed processing orders move to ResultadoIncerto.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Adapters/ExecutionStore.cs","locator":"RecuperarCrashAsync","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:outbox-cas-guard","subject":"capability:accept-pix-request","relation":"constrained_by","object":"rule:outbox-cas-lease","statement":"OutboxRelay marks PUBLISHED strictly with active CAS lease match.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Adapters/OutboxAndAuthAdapters.cs","locator":"MarcarPublicadoAsync","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:anti-ssrf-guard","subject":"capability:accept-pix-request","relation":"constrained_by","object":"rule:anti-ssrf-ipv6-mapped","statement":"CallbackDomain rejects IPv4-mapped IPv6 addresses.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Callbacks.Domain/CallbackDomain.cs","locator":"ValidarIpAddress","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:accept-contract-exposes","subject":"capability:accept-pix-request","relation":"exposes","object":"contract:accept-endpoint","statement":"AcceptRequestUseCase returns AceiteOriginal.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Application/Features/AcceptRequest/AcceptRequestUseCase.cs","locator":"ExecuteAsync","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:accept-governed-outbox","subject":"capability:accept-pix-request","relation":"governed_by","object":"decision:outbox-pattern","statement":"Atomic outbox persistence.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Adapters/AcceptanceStore.cs","locator":"OutboxRecord saved","snapshot":null}],"derived_from":[],"gap":null}]}}
```

# Modernização da Plataforma Pix
Core business feature providing asynchronous, resilient, and secure Pix processing aligned with enterprise financial standards.

## OVERVIEW
Pix Cash-Out and Billing processing in .NET 8 with atomic idempotency, FIFO Service Bus sessions, OutboxRelay with `WITH (UPDLOCK, READPAST)` in Azure SQL Server (Dapper/DbUp), CAS lease fencing, and fail-closed Redis admission. Supports read-replica offloading, resilient cache fallback, OIDC auth, and 2s SPI timeouts across 138 verified tests.

## FOLDER STRUCTURE
<folder_structure>
```
src/
├── Pix.Processing.*/     # Domain, vertical use cases, Dapper/DbUp persistence
├── Pix.Billing.*/        # Billing models, use cases, persistence
├── Pix.Callbacks.*/      # Webhooks and safe transport
├── *.Api/                # CashOut and Payment HTTP APIs
└── *.Worker/             # Outbank, Reconciliation, Webhook workers
```
</folder_structure>

## KEY MECHANISMS
- **Terminal State Precedence (PIX-AUD-01)**: Replay returns `JaConcluido` before checking intention.
- **Crash Recovery (PIX-AUD-02)**: Stranded `EmProcessamento` with expired lease moves to `ResultadoIncerto`.
- **Autonomous READY Dispatch (PIX-AUD-03)**: Scheduled scanner dispatches ready pending commands.
- **Outbox Integrity (PIX-AUD-04/05)**: Concrete serialization and CAS lease verification prevent phantom publish.
- **Anti-SSRF & DNS Pinning (PIX-AUD-06, INV-15)**: IPv4-mapped IPv6 normalization and socket IP pinning prevent SSRF.

## HOW TO EXECUTE PIX WORKFLOW
1. POST `/api/v1/pix/cash-out` with idempotency key.
2. Validate Redis admission and payload fingerprint.
3. Commit `PedidoPix`, `IdempotencyRecord`, `OutboxRecord` atomically (`202 Accepted`).
4. `Outbank.Connector` processes message by session, acquires lease, and dispatches to Outbank.

<code_example>
# CORRECT: Atomic persistence with idempotency fingerprint verification
var res = await _acceptanceStore.ConfirmarAceiteAsync(pedido, chave, fp, factory, true, ct);
</code_example>

## PARAMETERS / CONFIGURATIONS
| Name | Type | Description |
|------|------|-------------|
| ConnectionStrings:Sql | string | Azure SQL Server connection string |
| ConnectionStrings:Redis | string | Redis connection string (fail-closed) |
| ConnectionStrings:ServiceBus | string | Azure Service Bus connection string |

## BEST PRACTICES
REQUIRED: Check terminal state in `ExecutionStore` returning `JaConcluido`.
REQUIRED: Recover crashed orders with expired leases to `ResultadoIncerto`.
REQUIRED: Validate payload fingerprint on idempotency replay.
REQUIRED: Execute account commands sequentially via `SessionId = ContaId`.
REQUIRED: Fetch outbox records with `WITH (UPDLOCK, READPAST)` via CAS lease.
PROHIBITED: Permissive rate limiting when Redis is offline.
PROHIBITED: Dispatching external banking calls on sequence gaps.

## REFERENCES
- [**ARCHITECTURE.md**](../adr/ARCHITECTURE.md): System architecture, layers, and persistence patterns.
- [**TESTS.md**](../adr/TESTS.md): Testing strategies, test suites, and execution commands.
- [**qa_pipeline.md**](./qa_pipeline.md): Continuous testing pyramid and quality pipeline specifications.
