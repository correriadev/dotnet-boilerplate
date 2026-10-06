---
doc_type: adr
domain: architecture
stack: [.NET 8, Azure SQL Server, Dapper, DbUp, Redis, Azure Service Bus, Polly v8, GitHub Actions, Docker, Cosign, ORAS]
node_id: "adr:architecture"
tags: [architecture, hexagonal, outbox, redis, azure-service-bus, dapper, dbup, azure-sql, cicd, pix-migrator]
edges:
  - relation: references
    target: "adr:tests"
updated: 2026-10-06
---
# Project Architecture

## OVERVIEW
Hexagonal architecture with vertical slices in .NET 8 aligned with enterprise financial standards. Inbound HTTP requests enforce OIDC perimeter validation (`IOidcAuthValidator` returning 401) and orchestrate use cases interacting with Azure SQL Server 2022 (Dapper/DbUp in `[pix]`), Redis 7, and Azure Service Bus. Enforces CAS leases, durable sequencing, strict read-replica ledger integrity, thread-safe `RefCountedLock` concurrency, bounded `IMemoryCache` (SizeLimit) fallback, and T-SQL OutboxRelay (`TOP` with `UPDLOCK, READPAST`). Continuous delivery packages 6 immutable container images with Cosign keyless signatures, ORAS OCI release manifests, and manual approval gates for Homologation and Production.

## FOLDER STRUCTURE
<folder_structure>
```
azure-cloud-estudos/
├── .github/workflows/                  # CI/CD pipelines (ci-pr, package-release, deploy-dev, promote-hml, promote-prod, rollback)
├── src/
│   ├── Pix.Processing.Domain/          # Core domain models, value objects, invariants
│   ├── Pix.Processing.Application/     # Vertical slice use cases, commands, ports
│   ├── Pix.Processing.Infrastructure/  # Azure SQL (Dapper, DbUp), Outbox, Redis, ACL adapters
│   ├── Pix.Billing.Domain/             # Billing aggregate and validation
│   ├── Pix.Billing.Application/        # Billing generation use cases
│   ├── Pix.Billing.Infrastructure/     # Billing persistence
│   ├── Pix.Callbacks.Domain/           # Callback aggregate and retry tracking
│   ├── Pix.Callbacks.Application/      # Callback dispatch use cases
│   ├── Pix.Callbacks.Infrastructure/   # Callback persistence and IP-pinned safe transport
│   ├── Pix.CashOut.Api/                # Cash-out HTTP API with OIDC guards and health probes
│   ├── Pix.Payment.Api/                # Dynamic billing HTTP API with OIDC guards
│   ├── Pix.Migrator/                   # Console host for DbUp migrations, schema range checks, and live DB queries
│   ├── Outbank.Connector/              # Asynchronous worker processing Service Bus sessions
│   ├── Pix.Reconciliation.Worker/      # Worker triaging uncertain orders via CAS leases
│   └── Webhook.Dispatcher/             # Worker delivering callbacks with DNS SSRF filtering
└── tests/
    ├── Unit/                           # Domain, application, and CI/CD promotion gate test suites
    ├── Integration/                    # Persistence, concurrency, and broker resilience suites
    └── Architecture/                   # Architectural fitness rule enforcement suites
```
</folder_structure>

## LAYERS
- **Domain**: Pure business rules and aggregates (PedidoPix, CoordenacaoConta). PROHIBITED: Referencing Application, Infrastructure, or external frameworks.
- **Application**: Vertical slice use cases and port contracts (AcceptRequestUseCase, ExecutePaymentUseCase). PROHIBITED: Direct dependencies on Infrastructure or SQL drivers.
- **Infrastructure**: Implementations of persistence ports (Dapper, DbUp, OutboxRepository), Redis caching/rate limiting, and external ACLs.
- **Hosts (APIs & Workers)**: Minimal entrypoints configuring DI, OpenTelemetry W3C trace context, and health probes (/health/live, /health/ready).
- **Delivery & Migration Hosts**: Console host Pix.Migrator executando como ACA Job para transições de banco e workflows de entrega contínua com gates manuais.

## MODULES
| Module | Responsibility | Location |
|--------|-----------------|-------------|
| Pix.Processing.Domain | Entities, value objects, and lifecycle invariants for Pix orders. | `src/Pix.Processing.Domain/` |
| Pix.Processing.Application | Vertical slice use cases for acceptance, execution, and reconciliation. | `src/Pix.Processing.Application/` |
| Pix.Processing.Infrastructure | Azure SQL Dapper repositories, DbUp runner, Outbox, Redis, ACL adapters. | `src/Pix.Processing.Infrastructure/` |
| Pix.Billing.Domain / App / Infra | Dynamic QR code billing generation, validation, and persistence. | `src/Pix.Billing.*/` |
| Pix.Callbacks.Domain / App / Infra | Webhook dispatch aggregate, retry state machines, and safe transport. | `src/Pix.Callbacks.*/` |
| Pix.CashOut.Api / Pix.Payment.Api | HTTP edges with perimeter OIDC validation and health probes. | `src/Pix.*.Api/` |
| Pix.Migrator | Executable console host running DbUp migrations and schema range checks. | `src/Pix.Migrator/` |
| Workers | Outbank.Connector (sessions), Reconciliation.Worker (CAS), Webhook.Dispatcher (SSRF). | `src/*Worker*/`, `src/Outbank.*/` |

## PATTERNS
- **Terminal State Precedence (PIX-AUD-01)**: REQUIRED: Check terminal state in ExecutionStore before intention; return StatusAquisicao.JaConcluido on replay.
- **Crashed Order Recovery (PIX-AUD-02)**: REQUIRED: Transition EmProcessamento orders with expired lease to ResultadoIncerto; block CoordenacaoConta.
- **Transactional Outbox on Azure SQL (EV-01, INV-13)**: REQUIRED: OutboxRelay queries records via `TOP (@batchSize)` and `WITH (UPDLOCK, READPAST)` on `[pix].[OutboxRecords]`.
- **Strict Read-Replica Ledger Integrity**: REQUIRED: Query balance via `ApplicationIntent=ReadOnly`; return `0.00m` for unregistered accounts with zero synthetic balances.
- **Thread-Safe Account Concurrency**: REQUIRED: Protect account mutations using `RefCountedLock` in `AcceptanceStore` avoiding orphaned locks and memory leaks.
- **Fail-Closed Redis Admission (INV-18)**: REQUIRED: Reject traffic (StatusAdmissao.Indisponivel) when Redis is unavailable. Resilient bounded fallback via `IMemoryCache` (SizeLimit) and Circuit Breaker.
- **Perimeter OIDC Authentication**: REQUIRED: Validate JWT tokens via `IOidcAuthValidator`; return HTTP 401 Unauthorized on invalid/missing tokens; support Auth:SimulatedMode=true in Dev/CI.
- **SSRF Safe Transport (INV-15)**: REQUIRED: SafeCallbackTransport pins TCP socket to resolved non-private IP via ConnectCallback.
- **CI/CD Delivery Governance**: REQUIRED: Deploy automatic exclusively to Dev; Hml and Prod require manual approval gates; same 6 digests promoted without rebuild.
- **Schema Evolution Control (Expand/Contract)**: REQUIRED: Migrations executed via Pix.Migrator ACA job with 300s command timeout; reject simultaneous Expand and Contract for same feature.

<code_patterns>
# REQUIRED: Perimeter OIDC authentication validation in API controllers
if (!_authValidator.ValidateToken(authHeader))
    return Unauthorized(new { message = "Token OIDC inválido ou ausente." });

# REQUIRED: Azure SQL Server T-SQL Outbox batch query with skip-lock hints
var sql = @"SELECT TOP (@batchSize) EventId, EventType, Payload, SessionId, Retries
            FROM [pix].[OutboxRecords] WITH (UPDLOCK, READPAST)
            WHERE Status = 'PENDING' AND (ExpiresAt IS NULL OR ExpiresAt < @Now)";

# REQUIRED: Read replica strict ledger integrity returning 0 for missing accounts
var saldo = await connection.QuerySingleOrDefaultAsync<decimal?>(
    "SELECT SaldoDisponivel FROM [pix].[Saldos] WHERE ContaId = @ContaId", new { ContaId = contaId });
return saldo ?? 0.00m; // PROHIBITED: Fabricating synthetic balance

# REQUIRED: Thread-safe RefCountedLock per account in AcceptanceStore
var lockObj = _accountLocks.GetOrAdd(contaId, _ => new RefCountedLock());
await lockObj.WaitAsync(ct);
try { /* atomic persistence */ } finally { lockObj.Release(); }
</code_patterns>

## INTEGRATIONS
| External Service / Component | Purpose | Connection / Authentication Method |
|------------------------------|---------|-------------------------------------|
| Azure SQL Server 2022 | ACID store para pedidos, outbox e saldos via Dapper + DbUp no schema [pix]. Suporte a réplica síncrona com ApplicationIntent=ReadOnly. | Microsoft.Data.SqlClient com SqlTransaction e Dapper |
| Redis 7 | Admission rate limiting e cache com fallback resiliente para IMemoryCache bounded e Circuit Breaker. | StackExchange.Redis connection multiplexer |
| Azure Service Bus (Emulator) | Message broker para comandos financeiros com FIFO sessions (pix-cashout-requested, pix-cashout-completed). | Azure.Messaging.ServiceBus client com SessionId = ContaId |
| Outbank / SPI BACEN | Liquidação bancária externa com timeout estrito de 2s e backoff com jitter via Polly v8. | Outbank ACL Adapter com Microsoft.Extensions.Http.Resilience |
| Ping Identity / OIDC | Autenticação perimetral via IOidcAuthValidator / JWT Bearer com suporte a modo simulado (Auth:SimulatedMode=true) em Dev/CI. | Microsoft.AspNetCore.Authentication.JwtBearer |
| SafeCallbackTransport | HTTP client delivering webhooks com filtro DNS SSRF pinned ao socket. | SocketsHttpHandler |
| Azure Container Registry & ORAS | Armazenamento de imagens imutáveis e manifestos selados como artefatos OCI com assinatura Cosign keyless. | OIDC via GitHub Actions e ORAS CLI |

## REFERENCES
- [**TESTS.md**](./TESTS.md): Testing strategies, test suites, and execution commands.
- [**plataforma_pix.md**](../feature/plataforma_pix.md): Core Pix platform domain capabilities, contracts, rules, and execution flow.
- [**qa_pipeline.md**](../feature/qa_pipeline.md): Continuous testing pyramid and quality pipeline specifications.
- [**cicd_pipeline.md**](../feature/cicd_pipeline.md): Esteira CI/CD, gates manuais, proveniência Cosign e rollback determinístico.
