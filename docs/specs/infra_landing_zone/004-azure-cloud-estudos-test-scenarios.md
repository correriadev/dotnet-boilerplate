# Test Scenarios — azure-cloud-estudos

**Domain:** infra_landing_zone  
**Project:** azure-cloud-estudos  
**Framework:** xUnit 2.9.0, FluentAssertions 6.12.0  
**Date:** 2026-10-07  

---

## Section 1 — Unit Tests

### 1.1 Value Objects & Configurations

#### GatewayRouteConfig
- [ ] **TS-INFRA-01: Should create GatewayRouteConfig successfully when parameters are valid**
  - Given: `RouteId = "cashout-route"`, `ClusterId = "cashout-cluster"`, `MatchPath = "/api/v1/cashout/{**catch-all}"`
  - When: `new GatewayRouteConfig(...)` é instanciado
  - Then: Objeto é criado com propriedades imutáveis válidas.

- [ ] **TS-INFRA-02: Should reject GatewayRouteConfig when MatchPath is empty or does not start with slash**
  - Given: `MatchPath = "api/v1/cashout"` ou vazio
  - When: Tentativa de instanciação
  - Then: Lança `ArgumentException`.

#### SubnetTopology
- [ ] **TS-INFRA-03: Should validate SubnetTopology CIDR format**
  - Given: `Name = "snet-data"`, `AddressPrefix = "10.0.3.0/24"`
  - When: Topologia é validada
  - Then: Retorna sucesso; rejeita prefixos CIDR fora da máscara `/24` ou fora da VNet `10.0.0.0/16`.

#### SqlSyncReplicaTopology
- [ ] **TS-INFRA-04: Should enforce ReadScaleOut enabled for sync replica queries**
  - Given: Configuração do Azure SQL com `ReadScaleOut = true`
  - When: String de conexão de leitura é montada
  - Then: Contém obrigatoriamente a flag `ApplicationIntent=ReadOnly`.

### 1.2 Transform Providers

#### W3CHeaderTransformProvider
- [ ] **TS-INFRA-05: Should inject incoming traceparent into proxy request headers**
  - Given: Requisição HTTP de entrada contendo `traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01`
  - When: `W3CHeaderTransformProvider.ApplyW3CTransform` é executado
  - Then: O mesmo `traceparent` é anexado na requisição despachada ao cluster downstream.

- [ ] **TS-INFRA-06: Should generate new W3C traceparent when incoming request lacks trace header**
  - Given: Requisição HTTP de entrada sem cabeçalho `traceparent`
  - When: Transform é aplicado
  - Then: Novo `traceparent` W3C válido de 55 caracteres é gerado e repassado.

#### PerimeterAuthTransformProvider
- [ ] **TS-INFRA-07: Should preserve Authorization Bearer token without modification**
  - Given: Requisição contendo `Authorization: Bearer eyJhbGciOi...`
  - When: Transform de autenticação é executado
  - Then: O token Bearer é repassado idêntico para validação na API interna.

---

## Section 2 — Integration Tests

### 2.1 YARP Gateway Route Matching & Dispatch
- [ ] **TS-INFRA-08: Should route /api/v1/cashout requests to CashOutApi cluster**
  - Given: YARP Gateway em execução em test host
  - When: Requisição `POST /api/v1/cashout/solicitar` é recebida
  - Then: Proxy encaminha para o destino configurado do `ca-pix-cashout`.

- [ ] **TS-INFRA-09: Should route /api/v1/pix requests to PaymentApi cluster**
  - Given: YARP Gateway em execução em test host
  - When: Requisição `POST /api/v1/pix/cobranca` é recebida
  - Then: Proxy encaminha para o destino configurado do `ca-pix-payment`.

### 2.2 Health Probes & Gateway Lifecycle
- [ ] **TS-INFRA-10: Should return HTTP 200 on /health/live probe**
  - Given: YARP Gateway ativo
  - When: `GET /health/live` é chamado
  - Then: Retorna HTTP 200 OK com payload `{ "status": "Live" }`.

- [ ] **TS-INFRA-11: Should return HTTP 200 on /health/ready probe when routes and clusters are loaded**
  - Given: Rotas e clusters carregados com sucesso no provedor de memória
  - When: `GET /health/ready` é chamado
  - Then: Retorna HTTP 200 OK com status "Ready".

### 2.3 Bicep Template Static Validation
- [ ] **TS-INFRA-12: Should validate Bicep Landing Zone syntax and parameter constraints**
  - Given: Arquivos `infra/bicep/main.bicep`, `network.bicep`, `security.bicep`, `data.bicep`, `apps.bicep`
  - When: Validação estática de linter / compilação é executada
  - Then: Zero erros de sintaxe; todos os módulos resolvem dependências de outputs corretamente.

- [ ] **TS-INFRA-13: Should enforce no public IP assignment on data subnet in network.bicep**
  - Given: Template `network.bicep`
  - When: Regras de NSG e Private Endpoints são analisados
  - Then: Inspeciona que `snet-data` bloqueia 100% de tráfego de entrada da tag `Internet`.

---

## Section 3 — Functional Tests (End-to-End Flow)

### 3.1 Happy Path Flows
- [ ] **TS-INFRA-14: Should process full cash-out flow through Gateway preserving W3C context**
  - Given: Gateway YARP ouvindo na porta 8080 e CashOutApi ativo no downstream
  - When: Cliente envia `POST /api/v1/cashout` com cabeçalho `traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01` e Bearer JWT
  - Then: Resposta retorna HTTP 200/202, e trace downstream registra o mesmo TraceId `4bf92f3577b34da6a3ce929d0e0e4736`.

- [ ] **TS-INFRA-15: Should process dynamic billing creation through Gateway**
  - Given: Gateway YARP e PaymentApi ativos
  - When: Cliente envia `POST /api/v1/pix/cobranca`
  - Then: Retorna HTTP 201 Created com QR Code e identificador da cobrança.

### 3.2 Alternative and Error Flows
- [ ] **TS-INFRA-16: Should return HTTP 404 for unmapped route through Gateway**
  - Given: Gateway YARP ativo
  - When: Cliente envia requisição para `/api/v1/unknown-path`
  - Then: Gateway retorna HTTP 404 Not Found imediatamente.

- [ ] **TS-INFRA-17: Should return HTTP 503 or HTTP 504 when downstream service is unreachable**
  - Given: Cluster downstream com instâncias offline
  - When: Requisição é enviada para rota mapeada
  - Then: Gateway retorna HTTP 503/504 com tratamento limpo sem vazamento de stacktrace interno.

### 3.3 Security Scenarios
- [ ] **TS-INFRA-18: Should reject path traversal and malicious URL tampering at gateway boundary**
  - Given: Requisição contendo `../` ou caracteres de escape inválidos
  - When: Requisição atinge o Gateway
  - Then: Gateway bloqueia a requisição com HTTP 400 Bad Request.

- [ ] **TS-INFRA-19: Should exclude sensitive tokens and passwords from gateway logs and headers dump**
  - Given: Requisição contendo token JWT e chaves sensíveis
  - When: Gateway registra telemetria de tráfego
  - Then: Logs registram apenas método, rota, status e TraceId, sem expor o payload do token de autorização.
