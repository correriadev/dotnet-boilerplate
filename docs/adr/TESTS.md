---
doc_type: adr
domain: testing
stack: [.NET 8, xUnit 2.9.0, FluentAssertions 6.12.0, NSubstitute 5.1.0, Coverlet]
node_id: "adr:tests"
tags: [testing, unit-tests, integration-tests, architecture-tests, cicd-tests]
edges:
  - relation: references
    target: "adr:architecture"
updated: 2026-10-06
---
# Testing Protocol

## OVERVIEW
Estratégia abrangente de verificação com xUnit, FluentAssertions e NSubstitute totalizando 158 testes aprovados (75 Unit, 82 Integration, 1 Architecture) com 100% de sucesso. Valida invariantes do domínio Pix, persistência Dapper em Azure SQL Server 2022, integridade contábil na réplica síncrona, tolerância a falhas com cache bounded e Circuit Breaker, autenticação perimetral OIDC/JWT, exclusão mútua por conta com RefCountedLock, FIFO sessions no Azure Service Bus, governança de timeouts DDL no DbUp, esteira de CI/CD com gates de promoção, agregação de cobertura multi-projetos e circuit breaker no bake window.

## COMMANDS
| Type | Command | Description |
|------|---------|-------------|
| Unit | `dotnet test tests/Unit/Unit.csproj` | Executa 75 testes unitários cobrindo invariantes monetárias, regras de domínio e gates da esteira CI/CD. |
| Integration | `dotnet test tests/Integration/Integration.csproj` | Executa 82 testes de integração contra containers reais (SQL Server, Redis, Service Bus). |
| Architecture | `dotnet test tests/Architecture/Architecture.csproj` | Executa 1 teste de regras de fitness arquitetural e isolamento hexagonal. |
| All | `dotnet test azure-cloud-estudos.sln` | Executa a suíte completa de 158 testes consolidados em todas as camadas. |
| Coverage | `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover` | Gera relatórios de métricas de cobertura OpenCover (>85% global alcançado). |

## MINIMUM COVERAGE
REQUIRED: Manter os seguintes níveis mínimos de cobertura no quality gate:

| Layer | Coverage | Description |
|-------|----------|-------------|
| Domain / Core | 90% | Invariantes de negócio, value objects e regras de ciclo de vida |
| Application / Use Cases | 85% | Orquestração de fluxos, controle de admissão e interações com portas |
| Infrastructure / Adapters | 80% | Adaptadores de persistência, Outbox T-SQL e tradução ACL |
| Global | 85% | Cobertura média agregada do projeto |

## TEST SUITES & SPECIALIZED SCENARIOS
- **Autenticação Perimetral OIDC**: `ApiControllerAuthenticationTests` e `OidcAuthenticationTests` cobrindo guards HTTP 401 e validação criptográfica de tokens JWT.
- **Integridade Contábil na Réplica**: `ReadOnlyReplicaTests` garantindo leitura síncrona sem geração sintética de saldo.
- **Concorrência e Exclusão Mútua**: `ConcurrencyTests` validando `RefCountedLock` em `AcceptanceStore` sem locks órfãos nem memory leaks.
- **Resiliência e Fallback LRU**: `FallbackCacheAndResilienceTests` verificando tolerância a falhas do Redis e contenção de consumo via `IMemoryCache` (SizeLimit).
- **Mensageria e Broker**: `ServiceBusTopologyTests` cobrindo FIFO sessions por conta, dead-letter queues e detecção de duplicidade.
- **Esteira CI/CD e Governança de Release**: `CicdPipelineTests` cobrindo cenários TS-01 a TS-25 e rework (selagem de manifesto com 6 digests, gate Dev automático, gates Hml/Prod com aprovação manual, verificação de range de schema e rollback sem rebuild).

## PATTERNS & BEST PRACTICES
REQUIRED: Seguir o padrão AAA (Arrange, Act, Assert) com segregação clara em todos os cenários.
REQUIRED: Mockar exclusivamente fronteiras de portas e dependências externas, nunca entidades de domínio.
REQUIRED: Verificar rollback atômico sob falhas simuladas de banco de dados e broker.
REQUIRED: Testar contenção e assunção de leases expirados com avanço controlado de relógio.
REQUIRED: Validar abandono de mensagens no broker sob falhas transitórias para reprocessamento ordenado.
REQUIRED: Exigir 100% de aprovação nos 153 testes para promoção no quality gate.
PROHIBITED: Fallbacks permissivos de admissão em testes quando o Redis estiver simulado como offline.
PROHIBITED: Lógica de negócio em métodos de setup de testes ou construtores.
PROHIBITED: Testes dependentes de estado mutável compartilhado ou ordem não determinística.

## TOOLING
- **Framework:** xUnit 2.9.0
- **Assertions:** FluentAssertions 6.12.0
- **Mocks/Stubs:** NSubstitute 5.1.0
- **Coverage:** Coverlet / Visual Studio Test SDK 17.10.0
- **CI Integration:** Execução automatizada com `dotnet test` e tolerância zero a warnings (`TreatWarningsAsErrors=true`)

## TROUBLESHOOTING
- **Flaky tests:** Verificar saúde dos containers e portas no `docker-compose.yml`; garantir limpeza de chaves Redis e registros SQL.
- **Debug mode:** Executar `dotnet test --logger "console;verbosity=detailed"` para rastreamento detalhado de falhas.

## REFERENCES

- [**ARCHITECTURE.md**](./ARCHITECTURE.md): System architecture, layers, and persistence patterns.
- [**plataforma_pix.md**](../feature/plataforma_pix.md): Core Pix platform domain capabilities, contracts, rules, and execution flow.
- [**qa_pipeline.md**](../feature/qa_pipeline.md): Continuous testing pyramid and quality pipeline specifications.
- [**cicd_pipeline.md**](../feature/cicd_pipeline.md): Esteira CI/CD, gates manuais, proveniência Cosign e rollback determinístico.
