---
doc_type: feature
domain: qa_pipeline
stack: [.NET 8, Azure SQL Server, Dapper, DbUp, Redis, Azure Service Bus, Polly v8, xUnit]
node_id: "feature:qa-pipeline"
tags: [qa, testing, test-pyramid, oidc, outbox, azure-sql, integration-tests]
edges:
  - relation: implements
    target: "adr:architecture"
    read: must
  - relation: tested_by
    target: "adr:tests"
    read: must
updated: 2026-10-06
---
```graph
{"node_id":"feature:qa-pipeline","domain":"qa_pipeline","implements":["adr:architecture"],"tested_by":["adr:tests"],"entrypoints":["src/Pix.CashOut.Api/Program.cs","src/Pix.Payment.Api/Program.cs"],"registration_files":["src/Pix.CashOut.Api/Controllers/CashOutController.cs","src/Pix.Payment.Api/Controllers/BillingController.cs"],"reference_files":["src/Pix.Processing.Infrastructure/Auth/OidcAuthValidator.cs","src/Pix.Processing.Infrastructure/Adapters/AcceptanceStore.cs"],"code_files":["src/Pix.Processing.Infrastructure/Adapters/OutboxAndAuthAdapters.cs","src/Pix.Processing.Infrastructure/Adapters/RedisAndAclAdapters.cs","src/Pix.Processing.Infrastructure/Repositories/ReadOnlyReplicaRepository.cs","src/Pix.Processing.Infrastructure/PixDbContext.cs","src/Pix.Processing.Application/Features/AcceptRequest/AcceptRequestUseCase.cs"],"test_files":["tests/Integration/ApiControllerAuthenticationTests.cs","tests/Integration/OidcAuthenticationTests.cs","tests/Integration/ReadOnlyReplicaTests.cs","tests/Integration/ConcurrencyTests.cs","tests/Integration/FallbackCacheAndResilienceTests.cs"],"knowledge":{"schema_version":1,"entities":[{"id":"capability:continuous-test-pyramid","type":"capability","label":"Pyramid","definition":"Test pyramid covering unit, arch, and integration.","aliases":[]},{"id":"capability:oidc-perimetral-auth","type":"capability","label":"OIDC Auth","definition":"OIDC perimetral auth validation with JWT verification.","aliases":[]},{"id":"rule:strict-oidc-401-guard","type":"rule","label":"401 Guard","definition":"Unauthenticated requests return 401 Unauthorized.","aliases":[]},{"id":"rule:zero-synthetic-balance","type":"rule","label":"Zero Balance","definition":"Unregistered accounts return zero balance without synthetic data.","aliases":[]},{"id":"rule:tsql-skip-locked-relay","type":"rule","label":"T-SQL Locks","definition":"Outbox uses TOP and WITH (UPDLOCK, READPAST).","aliases":[]},{"id":"contract:oidc-validator-contract","type":"contract","label":"OIDC Contract","definition":"IOidcAuthValidator interface.","aliases":[]},{"id":"decision:pyramid-quality-gate","type":"decision","label":"Quality Gate","definition":"Gate requires 100% pass across 138 tests.","aliases":[]}],"claims":[{"id":"claim:oidc-auth-guard","subject":"capability:oidc-perimetral-auth","relation":"constrained_by","object":"rule:strict-oidc-401-guard","statement":"Controllers enforce 401 on invalid token.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.CashOut.Api/Controllers/CashOutController.cs","locator":"OIDC guard","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:replica-integrity-guard","subject":"capability:continuous-test-pyramid","relation":"constrained_by","object":"rule:zero-synthetic-balance","statement":"Replica returns 0.00m without synthetic balances.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Repositories/ReadOnlyReplicaRepository.cs","locator":"ObterSaldoAsync","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:outbox-tsql-relay","subject":"capability:continuous-test-pyramid","relation":"constrained_by","object":"rule:tsql-skip-locked-relay","statement":"OutboxRelay queries with TOP and UPDLOCK/READPAST.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Adapters/OutboxAndAuthAdapters.cs","locator":"ObterPendentesAsync","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:pyramid-governed-gate","subject":"capability:continuous-test-pyramid","relation":"governed_by","object":"decision:pyramid-quality-gate","statement":"Quality gate mandates 100% pass across 138 tests.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"tests/Integration/Integration.csproj","locator":"Test suite","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:oidc-exposes-contract","subject":"capability:oidc-perimetral-auth","relation":"exposes","object":"contract:oidc-validator-contract","statement":"IOidcAuthValidator exposes perimeter auth.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Processing.Infrastructure/Auth/OidcAuthValidator.cs","locator":"IOidcAuthValidator","snapshot":null}],"derived_from":[],"gap":null}]}}
```

# Pipeline de Qualidade e Pirâmide de Testes Contínua
Recurso de qualidade provendo execução contínua da pirâmide de testes e validação perimetral.

## OVERVIEW
Pipeline de qualidade contínua para a plataforma Pix em .NET 8. Orquestra 138 testes automatizados (55 Unit, 82 Integration, 1 Architecture) com 100% de aprovação contra containers reais (Azure SQL Server 2022, Redis 7, Service Bus Emulator). Valida perímetro de segurança OIDC com tokens criptográficos, locks transacionais T-SQL no Outbox, integridade contábil na réplica síncrona e controle de concorrência com caches bounded.

## FOLDER STRUCTURE
<folder_structure>
```
azure-cloud-estudos/
├── src/
│   ├── Pix.CashOut.Api/                  # CashOut HTTP API com OIDC perimeter guard
│   ├── Pix.Payment.Api/                  # Billing HTTP API com autenticação JWT
│   ├── Pix.Processing.Application/       # Casos de uso de aceitação e execução
│   └── Pix.Processing.Infrastructure/    # OIDC validator, Outbox T-SQL e ReadOnly replica
└── tests/
    ├── Architecture/                     # Fitness arquitetural e isolamento hexagonal
    ├── Unit/                             # Invariantes de domínio e validadores
    └── Integration/                      # OIDC, concorrência, réplica e resiliência
```
</folder_structure>

## ARCHITECTURAL LAYERS & CONTRACTS
- **Autenticação Perimetral OIDC**: Contrato `IOidcAuthValidator` injetado em `CashOutController` e `BillingController`. Header ausente ou token inválido gera HTTP 401 Unauthorized imediato.
- **OutboxRelay Azure SQL Server**: Consultas com `TOP (@batchSize)` e hints `WITH (UPDLOCK, READPAST)` na tabela `[pix].[OutboxRecords]`. Publicação exige lease ativo via CAS atômico.
- **Integridade Contábil na Réplica Síncrona**: `ReadOnlyReplicaRepository` executa consultas com `ApplicationIntent=ReadOnly`. Retorna `0.00m` para contas não cadastradas sem forjar saldos sintéticos.
- **Concorrência e Cache Resiliente**: `RefCountedLock` em `AcceptanceStore` elimina locks órfãos e memory leaks; `RedisFinancialCache` emprega `IMemoryCache` com `SizeLimit` bounded contra OOM.

## TEST PYRAMID TOPOLOGY
- **Nível 1 — Domínio e Unidade (55 testes)**: Invariantes monetárias, entidades Pix, juros/multas e limites operacionais sem I/O.
- **Nível 2 — Arquitetura (1 teste)**: NetArchTest garantindo fronteiras hexagonais estritas.
- **Nível 3 — Integração e Resiliência (82 testes)**:
  - *Segurança*: `ApiControllerAuthenticationTests` e `OidcAuthenticationTests` cobrindo guards 401 e validação JWT.
  - *Persistência e Concorrência*: `ReadOnlyReplicaTests` e `ConcurrencyTests` validando integridade e exclusão mútua por conta.
  - *Resiliência*: `FallbackCacheAndResilienceTests` verificando fallback do Redis para cache bounded.
  - *Mensageria*: `ServiceBusTopologyTests` cobrindo FIFO sessions e DLQ.

## HOW TO RUN QA SUITE
```bash
dotnet test azure-cloud-estudos.sln --logger "trx;LogFileName=test_results.trx"
dotnet test tests/Integration/Integration.csproj --filter "FullyQualifiedName~Authentication"
```

<code_example>
# CORRECT: Validação perimetral OIDC obrigatória antes do processamento
if (!_authValidator.ValidateToken(authHeader))
    return Unauthorized(new { message = "Token OIDC inválido ou ausente." });

# WRONG: Bypass de autenticação e retorno de saldo sintético sem consulta contábil
// return Ok(new SaldoResponse(contaId, 1000.00m)); // PROHIBITED
</code_example>

## PARAMETERS / CONFIGURATIONS
| Name | Type | Required | Description | Default |
|------|------|----------|-------------|---------|
| Auth:SimulatedMode | bool | Yes | Permite validação simplificada exclusivamente em testes locais e CI | false |
| ConnectionStrings:Sql | string | Yes | String de conexão para o Azure SQL Server 2022 | — |
| ConnectionStrings:Redis | string | Yes | String de conexão do Redis para admissão fail-closed | — |
| ConnectionStrings:ServiceBus | string | Yes | String de conexão para o Azure Service Bus ou emulador | — |

## BEST PRACTICES
REQUIRED: Interromper requisições sem credenciais válidas com HTTP 401 via `IOidcAuthValidator`.
REQUIRED: Utilizar `TOP (@batchSize)` e `WITH (UPDLOCK, READPAST)` no OutboxRelay sobre Azure SQL Server.
REQUIRED: Manter integridade contábil estrita na réplica de leitura retornando 0.00m sem fabricar saldos sintéticos.
REQUIRED: Limitar cache local com `SizeLimit` prevenindo OOM sob indisponibilidade do Redis.
REQUIRED: Assegurar 100% de aprovação nos 138 testes automatizados no quality gate.
PROHIBITED: Gerar registros sintéticos de saldo em contas inexistentes na réplica de leitura.
PROHIBITED: Utilizar sintaxe PostgreSQL (`FOR UPDATE SKIP LOCKED`) em queries destinadas ao Azure SQL Server.
PROHIBITED: Executar fallbacks permissivos em memória quando o Redis estiver inacessível.

## REFERENCES

- [**ARCHITECTURE.md**](../adr/ARCHITECTURE.md): System architecture, persistence, and messaging patterns.
- [**TESTS.md**](../adr/TESTS.md): Testing strategies, test suites, and execution commands.
- [**plataforma_pix.md**](../feature/plataforma_pix.md): Core Pix platform domain capabilities, contracts, rules, and execution flow.
