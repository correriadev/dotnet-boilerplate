---
doc_type: feature
domain: infra_landing_zone
stack: [.NET 8, YARP, Azure Bicep, Azure Virtual Network, Azure SQL, Azure Cache for Redis, Azure Service Bus, Azure Key Vault, Azure Container Apps, Docker]
node_id: "feature:infra-landing-zone"
tags: [infrastructure, landing-zone, bicep, yarp, zero-trust, azure-sql, nsg]
edges:
  - relation: implements
    target: "adr:architecture"
    read: must
  - relation: tested_by
    target: "adr:tests"
    read: must
updated: 2026-10-07
---
```graph
{"node_id":"feature:infra-landing-zone","domain":"infra_landing_zone","implements":["adr:architecture"],"tested_by":["adr:tests"],"entrypoints":["infra/bicep/main.bicep","src/Pix.Gateway/Program.cs"],"registration_files":["infra/bicep/parameters.dev.json"],"reference_files":["infra/bicep/network.bicep","src/Pix.Gateway/Middleware/SecurityAndAuditMiddleware.cs"],"code_files":["docker-compose.yml","infra/bicep/apps.bicep","infra/bicep/data.bicep","infra/bicep/modules/nsg-rules.bicep","infra/bicep/security.bicep","src/Pix.Gateway/Configuration/GatewayConfiguration.cs","src/Pix.Gateway/Dockerfile","src/Pix.Gateway/Transforms/PerimeterAuthTransformProvider.cs","src/Pix.Gateway/Transforms/W3CHeaderTransformProvider.cs","src/Pix.Gateway/appsettings.Development.json","src/Pix.Gateway/appsettings.json"],"test_files":["tests/Unit/GatewayTests.cs"],"knowledge":{"schema_version":1,"entities":[{"id":"capability:zero-trust-network-isolation","type":"capability","label":"Zero Trust Network Isolation","definition":"Segregação intra-VNet em subnets dedicadas com NSGs e Private Endpoints bloqueando 100% de tráfego de internet para dados.","aliases":["Network Isolation","VNet Segmentation"]},{"id":"capability:yarp-api-gateway","type":"capability","label":"YARP API Gateway","definition":"API Gateway reverso baseado em YARP .NET 8 centralizando roteamento, propagação W3C e repasse de autenticação perimetral.","aliases":["Yarp Gateway","Perimeter Reverse Proxy"]},{"id":"capability:azure-sql-read-replica","type":"capability","label":"Azure SQL Read Replica","definition":"Réplica de leitura síncrona habilitada com readScale para isolamento de consultas com ApplicationIntent=ReadOnly.","aliases":["Read-Scale Out","Replica Sincrona"]},{"id":"rule:strict-data-subnet-isolation","type":"rule","label":"Strict Data Subnet Isolation","definition":"Bloqueio obrigatório de 100% de acesso inbound da Internet e da subnet gateway para a subnet de dados, permitindo tráfego somente de snet-apps.","aliases":["Data NSG Rule","TS-INFRA-13"]},{"id":"rule:w3c-trace-propagation-and-generation","type":"rule","label":"W3C Trace Propagation and Generation","definition":"Preservação de cabeçalho traceparent válido de 55 caracteres e geração criptograficamente segura caso ausente ou zerado.","aliases":["W3C Trace Rule","TraceParent Validation"]},{"id":"rule:path-traversal-and-tampering-rejection","type":"rule","label":"Path Traversal Rejection","definition":"Rejeição com HTTP 400 Bad Request de tentativas de directory traversal sem falsos positivos em queries de intervalo.","aliases":["Traversal Filter","TS-INFRA-18"]},{"id":"contract:yarp-proxy-routing","type":"contract","label":"YARP Proxy Routing Contract","definition":"Mapeamento de rotas /api/v1/cashout/* para cashout-cluster e /api/v1/pix/* para payment-cluster preservando Authorization e W3C headers.","aliases":["Gateway Routing","Proxy Contract"]},{"id":"decision:hybrid-edge-architecture","type":"decision","label":"Hybrid Edge Architecture","definition":"Decisão arquitetural adotando YARP localmente no Docker Compose e Cloudflare na borda de nuvem conforme PBB F02.","aliases":["Hybrid Edge","Q01 Architecture Decision"]}],"claims":[{"id":"claim:zero-trust-isolation-rule","subject":"capability:zero-trust-network-isolation","relation":"constrained_by","object":"rule:strict-data-subnet-isolation","statement":"A subnet de dados rejeita 100% do tráfego vindo da Internet e da snet-gateway, admitindo conexões exclusivamente da snet-apps nas portas 1433, 6379, 5671-5672 e 443.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"infra/bicep/network.bicep","locator":"nsgData: Deny-Internet-Inbound and Deny-Gateway-And-Other-Vnet-Inbound","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:yarp-route-contract","subject":"capability:yarp-api-gateway","relation":"exposes","object":"contract:yarp-proxy-routing","statement":"O gateway expõe rotas HTTP /api/v1/cashout e /api/v1/pix com health probes /health/live e /health/ready, despachando para clusters downstream na porta 8080.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Gateway/Program.cs","locator":"MapReverseProxy and health probe endpoints","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:w3c-header-propagation","subject":"capability:yarp-api-gateway","relation":"constrained_by","object":"rule:w3c-trace-propagation-and-generation","statement":"O W3CHeaderTransformProvider valida e propaga o traceparent e tracestate recebidos ou gera um novo identificador W3C válido de 55 caracteres caso ausente ou todo zerado.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Gateway/Transforms/W3CHeaderTransformProvider.cs","locator":"ResolveOrGenerateTraceParent: IsValidW3CTraceParent check and fallback","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:security-middleware-traversal-guard","subject":"capability:yarp-api-gateway","relation":"constrained_by","object":"rule:path-traversal-and-tampering-rejection","statement":"O SecurityAndAuditMiddleware rejeita sequências de path traversal (.. e %2e%2e) no path e querystring retornando HTTP 400 sem bloquear consultas legítimas de intervalo.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Gateway/Middleware/SecurityAndAuditMiddleware.cs","locator":"IsPathTraversalAttempt and SanitizeLog","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:hybrid-edge-governance","subject":"capability:yarp-api-gateway","relation":"governed_by","object":"decision:hybrid-edge-architecture","statement":"A emulação local no Docker Compose opera com YARP (.NET 8) na porta 8080 e a borda remota no Azure é protegida por Cloudflare com WAF e terminação TLS.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"docker-compose.yml","locator":"pix-gateway service definition mapping port 8080","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:sql-read-scale-support","subject":"capability:azure-sql-read-replica","relation":null,"object":null,"statement":"O módulo Bicep data.bicep habilita readScale no Azure SQL Database (tier BusinessCritical BC_Gen5_2) para suportar balanceamento de leitura contábil via ApplicationIntent=ReadOnly.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"infra/bicep/data.bicep","locator":"sqlDatabase resource: readScale 'Enabled' and zoneRedundant true","snapshot":null}],"derived_from":[],"gap":null}]}}
```

# Infraestrutura Cloud-Native & Landing Zone Enterprise
Especificação técnica da infraestrutura como código (IaC Bicep), isolamento de rede Zero Trust e API Gateway YARP para a Plataforma Pix.

## OVERVIEW
Implementa a Landing Zone Enterprise corporativa no Microsoft Azure e a fachada de borda local para o ecossistema Pix. Estabelece uma arquitetura Zero Trust com segregação em 3 subnets dedicadas (`snet-gateway`, `snet-apps`, `snet-data`), Private Endpoints para recursos de dados/segurança sem IPs públicos, réplica de leitura síncrona no Azure SQL e o YARP Reverse Proxy (.NET 8) atuando como porta de entrada única com propagação W3C e repasse de autenticação perimetral OIDC.

## FOLDER STRUCTURE
<folder_structure>
```
azure-cloud-estudos/
├── infra/bicep/                        # Módulos Bicep declarativos da Landing Zone
│   ├── modules/                        # Submódulos reutilizáveis (NSGs e regras)
│   ├── network.bicep                   # VNet, 3 subnets, NSGs e Private DNS Zones
│   ├── security.bicep                  # Key Vault com RBAC, Private Endpoint e Managed Identity
│   ├── data.bicep                      # Azure SQL (ReadScale), Redis e Service Bus
│   ├── apps.bicep                      # Container Apps Environment integrado à snet-apps
│   └── main.bicep                      # Orquestrador raiz da Landing Zone
├── src/Pix.Gateway/                    # API Gateway reverso baseado em YARP (.NET 8)
│   ├── Configuration/                  # Value Objects de validação de rotas e topologia
│   ├── Transforms/                     # Provedores de injeção de trace W3C e repasse OIDC
│   └── Middleware/                     # Sanitização de logs e mitigação de path traversal
└── tests/Unit/                         # Suíte de testes unitários do Gateway e topologia
```
</folder_structure>

## MAIN COMPONENTS
- **Zero Trust Network Isolation**: Virtual Network `10.0.0.0/16` particionada em 3 subnets `/24`. A camada de dados (`snet-data`) rejeita 100% do tráfego da Internet e da subnet gateway, permitindo comunicação apenas de `snet-apps`.
- **Private Link & DNS**: Recursos PaaS (Azure SQL, Redis Cache, Service Bus e Key Vault) acessíveis estritamente via Private Endpoints na `snet-data` com zonas DNS privadas linkadas à VNet.
- **Azure SQL Sync Read Replica**: Database provisionado no tier `BusinessCritical` (`BC_Gen5_2`) com `readScale: Enabled`, viabilizando consultas contábeis na réplica secundária via `ApplicationIntent=ReadOnly`.
- **YARP Reverse Proxy**: Gateway corporativo (.NET 8) exposto na porta 8080 com roteamento dinâmico para os clusters `cashout-cluster` e `payment-cluster`, provendo health probes `/health/live` e `/health/ready`.
- **W3C Trace & Security Middleware**: Garante `traceparent` de 55 caracteres válido, bloqueia tentativas de path traversal (`..` e `%2e%2e`) e expurga segredos dos logs.

## HOW TO PROVISION AND EXECUTE

### Prerequisites
1. Azure CLI 2.60+ instalado com extensão Bicep configurada.
2. .NET 8 SDK e Docker Engine 24+ com Compose v2.

### Steps
1. Validar e simular o deployment dos templates Bicep com `az deployment group what-if`.
2. Provisionar a infraestrutura no Azure via `az deployment group create -f infra/bicep/main.bicep -p infra/bicep/parameters.dev.json`.
3. Executar o ecossistema com paridade local via `docker compose up --build pix-gateway`.

<code_example>
# CORRECT: Configuração de rota YARP com captura e health check
{
  "ReverseProxy": {
    "Routes": {
      "cashout-route": {
        "ClusterId": "cashout-cluster",
        "Match": { "Path": "/api/v1/cashout/{**catch-all}" }
      }
    }
  }
}

# WRONG: Rota sem prefixo de barra ou sem cluster associado
{
  "ReverseProxy": {
    "Routes": {
      "invalid-route": { "Match": { "Path": "api/v1/cashout" } }
    }
  }
}
</code_example>

## PARAMETERS / CONFIGURATIONS

| Parameter | Type | Required | Description | Default / Value |
|---|---|---|---|---|
| `environment` | string | Yes | Ambiente de implantação (`dev`, `hml`, `prod`) | `dev` |
| `workloadName` | string | Yes | Prefixo identificador dos recursos de nuvem | `pix` |
| `sqlAdminPassword` | string | Yes | Senha administrativa segura para o Azure SQL Server | Secure String |
| `snetGatewayPrefix`| string | No | Endereçamento CIDR da subnet do gateway | `10.0.1.0/24` |
| `snetAppsPrefix` | string | No | Endereçamento CIDR da subnet de aplicações | `10.0.2.0/24` |
| `snetDataPrefix` | string | No | Endereçamento CIDR da subnet de dados | `10.0.3.0/24` |
| `sqlSku` | string | No | Tier e computação do Azure SQL com réplica síncrona | `BC_Gen5_2` |

## BEST PRACTICES
REQUIRED: Bloquear 100% de IP público e acesso de internet na subnet `snet-data` via NSG dedicado.  
REQUIRED: Utilizar `ApplicationIntent=ReadOnly` na connection string para consultas de leitura na réplica secundária do Azure SQL.  
REQUIRED: Propagar o cabeçalho W3C `traceparent` original de 55 caracteres entre o Gateway e os microsserviços internos.  
REQUIRED: Validar e higienizar requisições HTTP contra path traversal e duplo URL-encoding no middleware de segurança.  
PROHIBITED: Atribuir IPs públicos a instâncias de banco de dados, filas ou caches.  
PROHIBITED: Expor credenciais administrativas ou senhas em texto plano nos arquivos Bicep ou repositórios.  

## TIPS
Utilize `az bicep build --file infra/bicep/main.bicep` para validar a integridade semântica e sintática dos módulos e referências antes de iniciar o provisionamento na assinatura do Azure.

## REFERENCES
- [**ARCHITECTURE.md**](../adr/ARCHITECTURE.md): Arquitetura global, padrões de rede e integração do ecossistema Pix.
- [**TESTS.md**](../adr/TESTS.md): Estratégias e comandos de testes unitários do Gateway e validação de infraestrutura.
- [**pbb_infra_landing_zone.md**](./pbb_infra_landing_zone.md): Backlog PBB com a matriz de requisitos e expectativas da Landing Zone.
