# Tactical Design — azure-cloud-estudos

**Domain:** infra_landing_zone | **Project:** azure-cloud-estudos

## Refinement Questions and Answers

| ID | Category | Question | Recommendation | Final Answer | Answered By |
|---|---|---|---|---|---|
| Q01 | architecture | Onde e como o serviço de API Gateway YARP (.NET 8) deve ser implementado no projeto? | Criar como `src/Pix.Gateway/` (.NET 8 com pacote `Yarp.ReverseProxy`), adicionado ao `azure-cloud-estudos.sln` e mapeado na porta 8080 no `docker-compose.yml` repassando OIDC e W3C headers. | Criar como `src/Pix.Gateway/` (.NET 8 com pacote `Yarp.ReverseProxy`), adicionado ao `azure-cloud-estudos.sln` e mapeado na porta 8080 no `docker-compose.yml` repassando OIDC e W3C headers. | human |
| Q02 | architecture | Como os templates Bicep da Landing Zone Enterprise e isolamento de rede devem ser estruturados? | Criar pasta `infra/bicep/` com orquestrador `main.bicep` e submódulos modulares: `network.bicep`, `security.bicep`, `data.bicep` e `apps.bicep`. | Criar pasta `infra/bicep/` com orquestrador `main.bicep` e submódulos modulares: `network.bicep`, `security.bicep`, `data.bicep` e `apps.bicep`. | human |
| Q03 | network | Qual padrão adotar para resolução de nomes dos Private Endpoints no Azure? | Azure Private DNS Zones oficiais (`privatelink.database.windows.net`, `privatelink.redis.cache.windows.net`, `privatelink.servicebus.windows.net`, `privatelink.vaultcore.azure.net`) linkadas à VNet. | Azure Private DNS Zones oficiais (`privatelink.database.windows.net`, `privatelink.redis.cache.windows.net`, `privatelink.servicebus.windows.net`, `privatelink.vaultcore.azure.net`) linkadas à VNet. | human |

---

## Section 1 — Main Structure

| Element | Layer / Type | Invariants / Tech Rules | 4-line Snippet |
|---|---|---|---|
| `GatewayConfiguration` | Gateway / ValueObject | Rotas e clusters YARP não nulos; portas válidas | *veja snippet abaixo* |
| `NetworkTopology` | Infrastructure / Model | 3 subnets dedicadas; sem IPs públicos em snet-data | *veja snippet abaixo* |
| `SecurityCustodyConfig` | Security / Model | Key Vault com RBAC habilitado; soft-delete ativo | *veja snippet abaixo* |
| `SyncReplicaConfig` | Data / Model | Azure SQL com réplica síncrona habilitada | *veja snippet abaixo* |

```csharp
public record GatewayRouteConfig(string RouteId, string ClusterId, string MatchPath);
// Invariante: MatchPath deve iniciar com barra e não ser vazio
// RouteId e ClusterId devem possuir correlação 1:1 com os microsserviços
```

```csharp
public record SubnetTopology(string Name, string AddressPrefix, string? NsgId);
// Invariante: AddressPrefix deve ser CIDR válido (/24)
// Subnets obrigatórias: snet-gateway, snet-apps, snet-data
```

```csharp
public record KeyVaultTopology(string VaultName, string Sku, bool EnableRbac);
// Invariante: Sku Standard ou Premium; EnableRbac = true obrigatório
// Soft-delete e purge protection ativados por conformidade
```

```csharp
public record SqlSyncReplicaTopology(string ServerName, string DbName, bool ReadScaleOut);
// Invariante: ReadScaleOut = true para roteamento ApplicationIntent=ReadOnly
// Tier General Purpose ou Business Critical
```

---

## Section 2 — Types / Interfaces

| Name | Layer | Rules | 4-line Snippet |
|---|---|---|---|
| `IGatewayHeaderTransformer` | Gateway / Transform | Preserva `traceparent`, `tracestate` e `Authorization` | *veja snippet abaixo* |
| `IInfrastructureValidator` | Infrastructure / Validation | Valida CIDR e isolamento de portas | *veja snippet abaixo* |

```csharp
public interface IGatewayHeaderTransformer {
    void TransformHeaders(HttpContext context, HttpRequestMessage targetRequest);
}
```

```csharp
public interface IInfrastructureValidator {
    bool ValidateSubnetIsolation(string sourceSubnet, string destSubnet, int port);
}
```

---

## Section 3 — Domain Services & Transform Pipelines

| Service | Responsibility | Coordinates | 4-line Snippet |
|---|---|---|---|
| `W3CHeaderTransformProvider` | Assegurar que cabeçalhos W3C recebidos sejam injetados nas chamadas de downstream | YARP Transform Pipeline | *veja snippet abaixo* |
| `PerimeterAuthTransformProvider` | Repassar `Authorization: Bearer` mantendo validação perimetral | YARP Transform Pipeline | *veja snippet abaixo* |

```csharp
public class W3CHeaderTransformProvider {
    public void ApplyW3CTransform(TransformBuilderContext context) =>
        context.AddRequestHeader("traceparent", ctx => ctx.HttpContext.Request.Headers["traceparent"]);
}
```

---

## Section 4 — Events

| Event | Trigger | Minimum Payload | Consumers |
|---|---|---|---|
| `GatewayStartedEvent` | YARP Gateway inicializado com sucesso | `{ port: 8080, routesCount: int }` | Logs / OpenTelemetry |
| `LandingZoneProvisionedEvent` | Execução do Bicep orquestrado concluída | `{ vnetId: string, subnetsCount: int }` | Azure Monitor / Pipeline CI |
| `PrivateEndpointLinkedEvent` | Private Endpoint associado à Private DNS Zone | `{ resourceName: string, privateIp: string }` | Auditoria SecOps |

---

## Section 5 — Data & Infrastructure Interfaces

| Resource | Methods | Return Types |
|---|---|---|
| `YarpReverseProxy` | MapReverseProxy, AddTransforms | `IEndpointConventionBuilder` |
| `BicepTemplates` | az deployment group create | `DeploymentOutputJson` |

```csharp
// Program.cs do YARP Gateway
app.MapReverseProxy(proxyPipeline => {
    proxyPipeline.UseLoadBalancing();
});
```

---

## Section 6 — Ordered Development Tasks

```json
[
  {
    "id": "01",
    "title": "Implementar Módulo Bicep de Rede e Isolamento (network.bicep)",
    "description": "Criar network.bicep definindo VNet 10.0.0.0/16, subnets segregadas (snet-gateway 10.0.1.0/24, snet-apps 10.0.2.0/24, snet-data 10.0.3.0/24), NSGs restritivos, Private Endpoints e Private DNS Zones.",
    "scope": [
      "infra/bicep/network.bicep",
      "infra/bicep/modules/nsg-rules.bicep"
    ],
    "acceptance": [
      "Subnet snet-data bloqueia entrada direta da internet e aceita tráfego apenas da snet-apps",
      "Private DNS Zones criadas e linkadas à VNet para sql, redis, servicebus e keyvault",
      "Template Bicep compila sem warnings com az bicep build"
    ],
    "depends_on": null
  },
  {
    "id": "02",
    "title": "Implementar Módulo Bicep de Segurança e Key Vault (security.bicep)",
    "description": "Criar security.bicep com Azure Key Vault Standard, RBAC ativo, Private Endpoint na snet-data e Managed Identity para os serviços Pix.",
    "scope": [
      "infra/bicep/security.bicep"
    ],
    "acceptance": [
      "Acesso público desabilitado (publicNetworkAccess = Disabled)",
      "Role assignment configurado com Key Vault Secrets User para Managed Identity",
      "Template compila sem erros de linter"
    ],
    "depends_on": "01"
  },
  {
    "id": "03",
    "title": "Implementar Módulo Bicep de Dados e Alta Disponibilidade (data.bicep)",
    "description": "Criar data.bicep com Azure SQL Database configurado com réplica síncrona secundária (readScale: Enabled), Redis Cache e Service Bus com Private Endpoints.",
    "scope": [
      "infra/bicep/data.bicep"
    ],
    "acceptance": [
      "Azure SQL Database possui readScale habilitado para suportar ApplicationIntent=ReadOnly",
      "Private Endpoints conectados à snet-data sem IPs públicos",
      "Template compila sem warnings"
    ],
    "depends_on": "02"
  },
  {
    "id": "04",
    "title": "Implementar Módulo Bicep de Aplicações e Root Orchestrator (apps.bicep e main.bicep)",
    "description": "Criar apps.bicep com Container Apps Environment integrado à snet-apps e main.bicep orquestrando todos os módulos com parâmetros de ambiente.",
    "scope": [
      "infra/bicep/apps.bicep",
      "infra/bicep/main.bicep",
      "infra/bicep/parameters.dev.json"
    ],
    "acceptance": [
      "main.bicep encadeia network -> security -> data -> apps passando referências entre outputs",
      "Parâmetros de ambiente dev suportam deploy idempotente",
      "Compilação completa com az bicep build produz JSON válido"
    ],
    "depends_on": "03"
  },
  {
    "id": "05",
    "title": "Criar Projeto YARP API Gateway (.NET 8) em src/Pix.Gateway",
    "description": "Criar o projeto Pix.Gateway.csproj (.NET 8 Web API mínima) com o pacote Yarp.ReverseProxy e adicionar à solução azure-cloud-estudos.sln.",
    "scope": [
      "src/Pix.Gateway/Pix.Gateway.csproj",
      "azure-cloud-estudos.sln"
    ],
    "acceptance": [
      "Projeto compila em .NET 8 com TreatWarningsAsErrors=true",
      "Referência ao pacote Yarp.ReverseProxy incluída",
      "Solução azure-cloud-estudos.sln atualizada e compilando sem erros"
    ],
    "depends_on": null
  },
  {
    "id": "06",
    "title": "Configurar Rotas, Clusters e Propagação de Cabeçalhos no YARP Gateway",
    "description": "Configurar rotas para CashOut (/api/v1/cashout/**) e Payment/Billing (/api/v1/pix/**) no appsettings.json e Program.cs, repassando traceparent e Authorization.",
    "scope": [
      "src/Pix.Gateway/Program.cs",
      "src/Pix.Gateway/appsettings.json",
      "src/Pix.Gateway/appsettings.Development.json"
    ],
    "acceptance": [
      "Rotas mapeadas para os clusters das APIs de CashOut e Payment",
      "Transforms configurados para propagar cabeçalhos W3C traceparent e Authorization Bearer",
      "Probes /health/live e /health/ready respondem HTTP 200"
    ],
    "depends_on": "05"
  },
  {
    "id": "07",
    "title": "Integrar YARP Gateway ao docker-compose.yml para Paridade Local",
    "description": "Adicionar o serviço pix-gateway no docker-compose.yml na porta 8080/8443 conectado à rede interna do ecossistema Pix.",
    "scope": [
      "docker-compose.yml",
      "src/Pix.Gateway/Dockerfile"
    ],
    "acceptance": [
      "Serviço pix-gateway exposto na porta 8080 dependendo de ca-pix-cashout e ca-pix-payment",
      "Dockerfile multi-stage .NET 8 criado para o Gateway",
      "Configuração válida do Compose sem conflitos de rede ou portas"
    ],
    "depends_on": "06"
  },
  {
    "id": "08",
    "title": "Implementar Testes Automatizados de Roteamento e Propagação do Gateway",
    "description": "Criar suíte de testes unitários e de integração validando transformações de cabeçalhos, health probes e integridade de roteamento do YARP Gateway.",
    "scope": [
      "tests/Unit/GatewayTests.cs",
      "tests/Integration/GatewayRoutingTests.cs"
    ],
    "acceptance": [
      "Testes validam que traceparent externo é preservado no forward",
      "Testes validam que token OIDC Authorization é repassado sem modificação",
      "100% dos testes aprovados com dotnet test"
    ],
    "depends_on": "07"
  }
]
```
