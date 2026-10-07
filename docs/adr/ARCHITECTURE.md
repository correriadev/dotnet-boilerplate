---
doc_type: adr
domain: architecture
stack: [.NET 8, Azure SQL Server, Dapper, DbUp, Redis, Azure Service Bus, YARP, Bicep, Azure Container Apps, Key Vault, Polly v8, OpenTelemetry, Dynatrace, GitHub Actions, Docker, Cosign, ORAS]
node_id: "adr:architecture"
tags: [architecture, hexagonal, zero-trust, yarp, bicep, outbox, redis, azure-service-bus, dapper, dbup, azure-sql, cicd, dynatrace]
edges:
  - relation: references
    target: "adr:tests"
updated: 2026-10-07
---
# Project Architecture

## OVERVIEW
Arquitetura hexagonal com vertical slices em .NET 8 e isolamento perimetral Zero Trust. O tráfego de entrada é unificado pelo YARP API Gateway com propagação W3C (`traceparent` de 55 caracteres) e repasse OIDC. Orquestra microsserviços no Azure Container Apps com persistência transacional ACID no Azure SQL Server 2022 (`[pix]` via Dapper e DbUp), réplica de leitura síncrona (`readScale: Enabled`), admissão fail-closed no Redis 7, comandos financeiros FIFO no Azure Service Bus e entrega contínua com gates Dynatrace SRG e Davis AI.

## FOLDER STRUCTURE
<folder_structure>
```
azure-cloud-estudos/
├── .github/workflows/                  # CI/CD pipelines (ci-pr, package-release, deploy-dev, promote-hml, promote-prod, rollback)
├── infra/bicep/                        # Módulos de Infraestrutura como Código (IaC) da Landing Zone
│   ├── network.bicep                   # VNet 10.0.0.0/16, subnets dedicadas, NSGs e Private DNS Zones
│   ├── security.bicep                  # Key Vault Standard RBAC, Private Endpoint e Managed Identity
│   ├── data.bicep                      # Azure SQL (ReadScale), Redis e Service Bus
│   ├── apps.bicep                      # Container Apps Environment integrado à snet-apps
│   └── main.bicep                      # Orquestrador mestre dos módulos de infraestrutura
├── src/
│   ├── Pix.Gateway/                    # API Gateway reverso baseado em YARP com W3C e guards
│   ├── Pix.Processing.Domain/          # Modelos, invariantes, regras puras e telemetria W3C
│   ├── Pix.Processing.Application/     # Casos de uso de aceitação, execução e conciliação
│   ├── Pix.Processing.Infrastructure/  # Adaptadores Azure SQL, Outbox, Redis, Dynatrace e ACLs
│   ├── Pix.Billing.*/                  # Domínio, aplicação e persistência de cobrança Pix
│   ├── Pix.Callbacks.*/                # Domínio, aplicação e transporte seguro de webhooks
│   ├── Pix.CashOut.Api/                # API HTTP de CashOut com guards OIDC e W3C
│   ├── Pix.Payment.Api/                # API HTTP de Billing dinâmico com guards OIDC
│   ├── Pix.Migrator/                   # Console host para migrações DbUp e checagem de schema
│   ├── Outbank.Connector/              # Worker consumindo Service Bus sessions e rastreio W3C
│   ├── Pix.Reconciliation.Worker/      # Worker de triagem de pedidos incertos via leases CAS
│   └── Webhook.Dispatcher/             # Worker de entrega de callbacks com filtro DNS SSRF
└── tests/
    ├── Unit/                           # Suítes de domínio, gates CI/CD, telemetria e Gateway YARP
    ├── Integration/                    # Suítes de persistência, concorrência e broker real
    └── Architecture/                   # Testes de fitness arquitetural e conformidade hexagonal
```
</folder_structure>

## LAYERS
- **Domain**: Regras puras, invariantes, agregados (PedidoPix, CoordenacaoConta) e modelos de telemetria (ContextoW3C, MetricasGoldenSignals, SanitizadorTelemetriaPolicy). PROHIBITED: Dependências de frameworks, banco de dados ou camadas externas.
- **Application**: Casos de uso e portas abstratas (AcceptRequestUseCase, ExecutePaymentUseCase). PROHIBITED: Dependência direta de drivers SQL ou bibliotecas de infraestrutura.
- **Infrastructure**: Implementações de portas (Dapper, DbUp, OutboxRepository), cache Redis, cliente Dynatrace e adaptadores de integração externa.
- **Gateway & Borda**: YARP API Gateway (.NET 8) atuando como ponto único de entrada HTTP com propagação de headers W3C, repasse de Bearer token OIDC, health probes e proteção contra path traversal.
- **Hosts (APIs & Workers)**: Pontos de entrada executáveis configurando DI, OpenTelemetry, sanitização de PII e health probes (/health/live, /health/ready).
- **Delivery & Migration Hosts**: Pix.Migrator em ACA Job para transições de banco e esteiras GitHub Actions com gates manuais.

## MODULES
| Module | Responsibility | Location |
|---|---|---|
| Pix.Gateway | API Gateway reverso YARP centralizando roteamento, W3C trace e repasse OIDC. | `src/Pix.Gateway/` |
| Pix.Processing.Domain | Entidades, value objects, invariantes financeiras e telemetria W3C. | `src/Pix.Processing.Domain/` |
| Pix.Processing.Application | Casos de uso de aceitação, execução e conciliação financeira. | `src/Pix.Processing.Application/` |
| Pix.Processing.Infrastructure | Repositórios Dapper, DbUp, Outbox T-SQL, Redis, Dynatrace SRG e OTLP. | `src/Pix.Processing.Infrastructure/` |
| Pix.Billing.* | Geração, persistência e validação de cobranças dinâmicas Pix (QR Code). | `src/Pix.Billing.*/` |
| Pix.Callbacks.* | Agregado de webhooks, máquina de retries e transporte seguro de entrega. | `src/Pix.Callbacks.*/` |
| Pix.CashOut.Api / Payment.Api | APIs HTTP de negócio com autenticação perimetral OIDC e telemetria W3C. | `src/Pix.*.Api/` |
| Pix.Migrator | Console host executando migrações DbUp com range dinâmico e validação pré-rollback. | `src/Pix.Migrator/` |
| Workers | Outbank.Connector (sessions), Reconciliation.Worker (CAS), Webhook.Dispatcher (SSRF). | `src/*Worker*/`, `src/Outbank.*/` |
| Infraestrutura Bicep | Landing Zone Enterprise: VNet 10.0.0.0/16, NSGs, Private Endpoints e Key Vault. | `infra/bicep/` |

## PATTERNS
- **Isolamento de Rede Zero Trust**: REQUIRED: Subnet de dados (`snet-data`) bloqueia 100% de tráfego de Internet e da subnet gateway; conexões aos Private Endpoints permitidas estritamente a partir da `snet-apps`.
- **API Gateway YARP e Borda Híbrida**: REQUIRED: YARP centraliza tráfego HTTP localmente na porta 8080 com health probes `/health/live` e `/health/ready`, enquanto a borda remota no Azure é protegida por Cloudflare com WAF e TLS.
- **Propagação W3C Trace Context (INV-20)**: REQUIRED: O gateway e os serviços preservam `traceparent` válido (55 chars) e `tracestate`; gera novo cabeçalho criptograficamente seguro caso ausente ou todo zerado.
- **Réplica de Leitura Síncrona Azure SQL**: REQUIRED: Consultas contábeis utilizam `ApplicationIntent=ReadOnly`; contas sem lançamentos retornam `0.00m` sem criação de saldos sintéticos.
- **Transactional Outbox T-SQL (EV-01, INV-13)**: REQUIRED: OutboxRelay consome registros via `TOP (@batchSize)` e `WITH (UPDLOCK, READPAST)` em `[pix].[OutboxRecords]`, preservando contexto W3C.
- **Admissão Fail-Closed no Redis (INV-18)**: REQUIRED: Rejeitar tráfego sob indisponibilidade do Redis, com fallback controlado via `IMemoryCache` bounded (SizeLimit) e Circuit Breaker.
- **Autenticação OIDC Perimetral**: REQUIRED: `IOidcAuthValidator` valida tokens JWT retornando HTTP 401; repassado intacto pelo YARP Gateway (`Authorization: Bearer`).
- **Mitigação de Path Traversal**: REQUIRED: `SecurityAndAuditMiddleware` rejeita sequências `..` e `%2e%2e` no path e query com HTTP 400, tolerando consultas de intervalo legítimas.
- **Sanitização de PII em Telemetria e Logs**: REQUIRED: Expurgo de CPFs, emails, senhas e tokens Bearer antes do envio a logs ou ao coletor OpenTelemetry/Dynatrace.

<code_patterns>
# REQUIRED: Validação de traceparent W3C com tratamento de zeros no Gateway
if (!W3CHeaderTransformProvider.IsValidW3CTraceParent(incomingTrace))
    outgoingTrace = W3CHeaderTransformProvider.GenerateW3CTraceParent();

# REQUIRED: Azure SQL Server T-SQL Outbox batch query com skip-lock
var sql = @"SELECT TOP (@batchSize) EventId, EventType, Payload, SessionId, Retries
            FROM [pix].[OutboxRecords] WITH (UPDLOCK, READPAST)
            WHERE Status = 'PENDING' AND (ExpiresAt IS NULL OR ExpiresAt < @Now)";

# REQUIRED: Isolamento estrito de NSG Bicep bloqueando Internet na snet-data
access: 'Deny'
sourceAddressPrefix: 'Internet'
destinationAddressPrefix: snetDataPrefix

# PROHIBITED: Exposição de senha em texto plano ou IP público em recursos PaaS
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  properties: { publicNetworkAccess: 'Disabled' }
}
</code_patterns>

## INTEGRATIONS
| External Service / Component | Purpose | Connection / Authentication Method |
|---|---|---|
| YARP API Gateway | Roteador reverso unificado, injeção W3C e repasse de auth. | Microsoft.ReverseProxy (.NET 8) na porta 8080 |
| Azure Virtual Network & NSGs | Landing Zone com isolamento Zero Trust em 3 subnets (/24). | Bicep Network Module com Private Endpoints |
| Azure SQL Server 2022 | Armazenamento ACID transacional e réplica de leitura síncrona. | Dapper + DbUp no schema [pix] com Private Endpoint |
| Redis 7 | Controle de admissão e rate limiting com fallback resiliente. | StackExchange.Redis com Private Endpoint |
| Azure Service Bus | Broker para comandos financeiros com FIFO sessions. | Azure.Messaging.ServiceBus com Private Endpoint |
| Azure Key Vault | Custódia de certificados mTLS e segredos com RBAC estrito. | Managed Identity com Secrets Store CSI e Private Endpoint |
| Dynatrace (SRG & Davis AI) | Quality gate de SLO em Hml e detecção de anomalias em Prod. | OTLP HTTP (porta 4318) + Dynatrace API v2 |
| Outbank / SPI BACEN | Liquidação bancária externa com timeout de 2s e Polly v8. | Outbank ACL Adapter com Microsoft.Extensions.Http |

## REFERENCES
- [**README.md**](../README.md): Índice principal da documentação técnica.
- [**TESTS.md**](./TESTS.md): Estratégias de testes, suítes unitárias, de integração e infraestrutura.
- [**infra_landing_zone.md**](../feature/infra_landing_zone.md): Especificação detalhada da Landing Zone e API Gateway YARP.
- [**plataforma_pix.md**](../feature/plataforma_pix.md): Domínio Pix, contratos, invariantes e fluxo de execução.
- [**qa_pipeline.md**](../feature/qa_pipeline.md): Pipeline de testes contínuos e verificação de perímetro.
- [**cicd_pipeline.md**](../feature/cicd_pipeline.md): Esteira CI/CD, proveniência Cosign e rollback automatizado.
- [**observabilidade_dynatrace.md**](../feature/observabilidade_dynatrace.md): Observabilidade de produção e quality gates com Davis AI.
