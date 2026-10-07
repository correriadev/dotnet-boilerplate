---
doc_type: adr
domain: testing
stack: [.NET 8, xUnit 2.9.0, FluentAssertions 6.12.0, NSubstitute 5.1.0, Coverlet]
node_id: "adr:tests"
tags: [testing, unit-tests, integration-tests, architecture-tests, gateway-tests, cicd-tests, telemetry-tests]
edges:
  - relation: references
    target: "adr:architecture"
updated: 2026-10-07
---
# Testing Protocol

## OVERVIEW
Estratégia de garantia de qualidade totalizando 200 testes aprovados (117 Unit, 82 Integration, 1 Architecture) com 100% de sucesso. Valida invariantes do domínio financeiro, persistência transacional Dapper, integridade da réplica síncrona Azure SQL, isolamento de rede Zero Trust, sintaxe Bicep, API Gateway YARP com propagação W3C e segurança de perímetro, esteira CI/CD com gates de promoção e observabilidade com Dynatrace SRG e Davis AI.

## COMMANDS
| Type | Command | Description |
|---|---|---|
| Unit | `dotnet test tests/Unit/Unit.csproj` | Executa 117 testes unitários (domínio, CI/CD, telemetria e Gateway YARP). |
| Integration | `dotnet test tests/Integration/Integration.csproj` | Executa 82 testes de integração contra containers reais (SQL Server, Redis, Service Bus). |
| Architecture | `dotnet test tests/Architecture/Architecture.csproj` | Executa 1 teste de fitness arquitetural e conformidade hexagonal. |
| All | `dotnet test azure-cloud-estudos.sln` | Executa a suíte consolidada de 200 testes em todas as camadas. |
| Coverage | `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover` | Gera relatório de cobertura OpenCover multi-projeto (>85% global). |

## MINIMUM COVERAGE
REQUIRED: Manter os seguintes níveis mínimos de cobertura no quality gate:

| Layer | Coverage | Description |
|---|---|---|
| Domain / Core | 90% | Invariantes de negócio, value objects e regras de ciclo de vida |
| Application / Use Cases | 85% | Orquestração de fluxos, controle de admissão e interações com portas |
| Infrastructure / Adapters | 80% | Adaptadores de persistência, Outbox T-SQL e Gateway YARP |
| Global | 85% | Cobertura média consolidada do ecossistema |

## TEST SUITES & SPECIALIZED SCENARIOS
- **Gateway & Infraestrutura Cloud-Native (`GatewayTests.cs` — TS-INFRA-01 a 19)**:
  - *Topologia e Configurações (TS-INFRA-01 a 04)*: Validação de rotas YARP, validação de CIDR de subnets (`/24` contido em `10.0.0.0/16`) e injeção de `ApplicationIntent=ReadOnly` sob réplica síncrona habilitada.
  - *Provedores de Transformação (TS-INFRA-05 a 07)*: Injeção de `traceparent` W3C de 55 caracteres, cura e geração de novo identificador para headers ausentes ou zerados, e preservação do token `Authorization: Bearer`.
  - *Roteamento e Health Probes (TS-INFRA-08 a 11)*: Despacho correto para `cashout-cluster` e `payment-cluster`, HTTP 200 em `/health/live` e `/health/ready` com clusters carregados.
  - *Validação Estática Bicep (TS-INFRA-12 a 13)*: Presença dos 5 módulos Bicep, ausência de senhas hardcoded em `data.bicep` e bloqueio estrito de 100% de Internet Inbound na `snet-data`.
  - *Fluxos End-to-End e Erros (TS-INFRA-14 a 17)*: Fluxo completo de CashOut e Billing pelo Gateway com W3C, retorno 404 para rotas não mapeadas e 503 para serviços indisponíveis.
  - *Segurança de Perímetro (TS-INFRA-18 a 19)*: Bloqueio de path traversal (`..` e `%2e%2e`) com HTTP 400 sem falsos positivos em queries de range (`?range=1..50`), e expurgo de Bearer tokens e senhas nos logs do Gateway.
- **Autenticação Perimetral OIDC**: `ApiControllerAuthenticationTests` e `OidcAuthenticationTests` cobrindo guards HTTP 401 e validação de tokens JWT.
- **Integridade Contábil na Réplica**: `ReadOnlyReplicaTests` garantindo leitura síncrona sem geração sintética de saldo.
- **Concorrência e Exclusão Mútua**: `ConcurrencyTests` validando `RefCountedLock` em `AcceptanceStore` sem locks órfãos.
- **Resiliência e Fallback LRU**: `FallbackCacheAndResilienceTests` verificando tolerância a falhas do Redis e contenção via `IMemoryCache` bounded.
- **Mensageria e Broker**: `ServiceBusTopologyTests` cobrindo FIFO sessions por conta e dead-letter queues.
- **Esteira CI/CD e Governança**: `CicdPipelineTests` cobrindo cenários TS-01 a TS-25 (selagem de 6 digests, gate Dev automático, aprovação manual Hml/Prod e rollback determinístico).
- **Observabilidade Dynatrace e Telemetria W3C**: `TelemetryTests` cobrindo cenários TS_OBS_01 a TS_OBS_21 (quality gate SRG fail-closed, janela de bake com Davis AI, validação de W3C e sanitização de PII).

## PATTERNS & BEST PRACTICES
REQUIRED: Seguir o padrão AAA (Arrange, Act, Assert) com segregação evidente.  
REQUIRED: Mockar exclusivamente fronteiras de portas externas, nunca entidades de domínio.  
REQUIRED: Validar mitigação de path traversal e expurgo de segredos em logs de borda.  
REQUIRED: Exigir 100% de aprovação nos 200 testes para aprovação no quality gate.  
PROHIBITED: Deixar senhas ou segredos hardcoded em arquivos de configuração ou testes.  
PROHIBITED: Permitir testes dependentes de ordem de execução ou estado compartilhado mutável.  

## TOOLING
- **Framework:** xUnit 2.9.0
- **Assertions:** FluentAssertions 6.12.0
- **Mocks/Stubs:** NSubstitute 5.1.0
- **Coverage:** Coverlet / Visual Studio Test SDK 17.10.0
- **CI Integration:** Execução automatizada com `dotnet test` e `TreatWarningsAsErrors=true`

## TROUBLESHOOTING
- **Flaky tests:** Verificar conectividade com os containers do `docker-compose.yml` e portas `1433`, `6379`, `5672` e `8080`.
- **Debug mode:** Executar `dotnet test --logger "console;verbosity=detailed"` para rastreamento detalhado de falhas.

## REFERENCES
- [**README.md**](../README.md): Índice geral da documentação do projeto.
- [**ARCHITECTURE.md**](./ARCHITECTURE.md): Arquitetura do sistema, isolamento Zero Trust e padrões de persistência.
- [**infra_landing_zone.md**](../feature/infra_landing_zone.md): Especificação da Landing Zone Enterprise e Gateway YARP.
- [**plataforma_pix.md**](../feature/plataforma_pix.md): Domínio Pix, contratos e regras de aceitação financeira.
- [**qa_pipeline.md**](../feature/qa_pipeline.md): Pipeline contínuo de qualidade e pirâmide de testes.
- [**cicd_pipeline.md**](../feature/cicd_pipeline.md): Esteira CI/CD, proveniência Cosign e rollback determinístico.
- [**observabilidade_dynatrace.md**](../feature/observabilidade_dynatrace.md): Observabilidade de produção e quality gates com Davis AI.
