# Strategic Design — Context Map (infra_landing_zone)

## Section 1 — Bounded Context Identification

| Bounded Context | Responsibility | Boundary (excluded) | Team Ownership | Key Entities |
|---|---|---|---|---|
| **GatewayBordaContext** | Centralizar a fachada de entrada HTTP da plataforma Pix, orquestrando roteamento reverso via YARP (.NET 8), validação perimetral e repasse transparente de cabeçalhos W3C e OIDC. | Não executa lógica de negócio bancária nem persiste dados transacionais de pagamentos. | Time de Plataforma / Core Backend | `ConfiguracaoGateway`, `RotaProxy`, `ClusterDestino`, `CabecalhoPropagacao` |
| **RedeConectividadeContext** | Declarar e manter a topologia de rede isolada da Landing Zone (VNet, subnets snet-gateway, snet-apps e snet-data, NSGs e Private Endpoints com Private DNS Zones). | Não gerencia regras de aplicação nem conteúdo interno de bancos de dados. | Time de Cloud Platform & SecOps | `TopologiaVNet`, `SubnetDefinition`, `RegraNSG`, `PrivateEndpointLink`, `PrivateDnsZone` |
| **TopologiaDadosContext** | Provisionar e estruturar os armazenadores de dados em nuvem com alta disponibilidade, incluindo Azure SQL com réplica de leitura síncrona, Redis e Service Bus. | Não define schemas relacionais de tabelas de domínio (responsabilidade do Pix.Migrator). | Time de Data Platform & DBA | `InstanciaAzureSql`, `ReplicaSincronaConfig`, `NamespaceServiceBus`, `CacheRedisConfig` |
| **CustodiaSegredosContext** | Proteger credenciais sensíveis, segredos de conexão e certificados digitais mTLS através do Azure Key Vault e autenticação por Managed Identity. | Não gerencia autorização de usuários finais da API Pix (responsabilidade do OIDC/Ping Identity). | Time de SecOps / Security Engineering | `KeyVaultVault`, `PoliticaRBAC`, `CertificadoMtlsBacen`, `ManagedIdentityBinding` |

---

## Section 2 — Context Map & Integration Relationships

### Mapeamento de Relações

```
[GatewayBordaContext] → [Pix.Processing (CashOut/Payment APIs)]
Pattern      : Open Host Service (OHS) / Published Language (PL)
Direction    : Upstream (Gateway) / Downstream (APIs)
Justification: O Gateway YARP atua como a fachada perimetral estável de entrada, roteando requisições REST externas padronizadas para as APIs internas.

[GatewayBordaContext] → [RedeConectividadeContext]
Pattern      : Customer-Supplier
Direction    : Upstream (Rede) / Downstream (Gateway)
Justification: O Gateway depende do provisionamento da sub-rede snet-gateway e regras de NSG fornecidas pela infraestrutura de rede.

[Pix.Processing (CashOut/Payment APIs)] → [TopologiaDadosContext]
Pattern      : Customer-Supplier
Direction    : Upstream (Topologia de Dados) / Downstream (APIs)
Justification: As APIs e workers consomem as connection strings e endpoints privados do Azure SQL (primário e réplica de leitura), Redis e Service Bus.

[Pix.Processing (APIs & Workers)] → [CustodiaSegredosContext]
Pattern      : Anti-Corruption Layer (ACL)
Direction    : Downstream (APIs) / Upstream (Key Vault)
Justification: Os serviços utilizam Managed Identity e Secrets Store CSI Driver para obter credenciais e certificados mTLS sem expor tokens nos artefatos.

[RedeConectividadeContext] → [TopologiaDadosContext]
Pattern      : Partnership
Direction    : Bidirectional
Justification: Private Endpoints e Private DNS Zones co-evoluem estritamente associados aos recursos de dados provisionados no Azure.
```

---

## Section 3 — Core Domain Highlight

```
Context   : GatewayBordaContext
Reason    : É a primeira linha de defesa e orquestração perimetral da plataforma Pix. Garante a unificação das APIs de CashOut e Billing sob uma fachada única de alta performance (.NET 8 YARP), propagando o contexto de rastreabilidade W3C (traceparent) essencial para compliance BACEN e auditoria ponta a ponta sem degradação de latência.
Investment: Implementação rigorosa com arquitetura limpa, configuração declarativa desacoplada, tratamento de timeout resiliente e suíte de testes unitários e de integração validando propagação de cabeçalhos e roteamento local.
```

---

## Section 4 — Architectural Decisions (ADRs)

### ADR-01: Adoção do YARP (.NET 8) como API Gateway Reverso Local e Fachada
- **Decision:** Implementar o projeto `src/Pix.Gateway/` utilizando a biblioteca oficial da Microsoft `Yarp.ReverseProxy` no .NET 8, integrado à solução e ao `docker-compose.yml` na porta 8080.
- **Context:** A plataforma Pix possui múltiplos microsserviços (CashOut e Payment) que necessitam de uma fachada perimetral unificada que propague cabeçalhos de autorização OIDC e rastreamento W3C sem introduzir soluções pesadas ou incompatíveis com o runtime .NET.
- **Consequences:** (+) Desempenho nativo com baixíssimo consumo de memória; integração perfeita com C# e OpenTelemetry; (+) Permite desenvolvimento e testes 100% offline no Docker Compose; (-) Requer configuração cuidadosa de rotas e clusters no `appsettings.json`.

### ADR-02: Modularização Declarativa da Landing Zone com Bicep em `infra/bicep/`
- **Decision:** Estruturar o provisionamento de nuvem em `infra/bicep/` com orquestrador `main.bicep` e submódulos dedicados: `network.bicep`, `security.bicep`, `data.bicep` e `apps.bicep`.
- **Context:** O provisionamento corporativo no Azure exige separação de responsabilidades entre rede, segurança e dados, garantindo reprodutibilidade, idempotência e parâmetros segregados por ambiente (`dev`, `hml`, `prod`).
- **Consequences:** (+) Facilidade de manutenção e isolamento de blast radius em alterações de infraestrutura; (+) Compatibilidade nativa com Azure CLI (`az deployment group create`); (-) Requer gestão de outputs entre módulos (ex: IDs de subnets passados para módulos de dados).

### ADR-03: Isolamento Zero Trust com Private Endpoints e Private DNS Zones
- **Decision:** Eliminar acessos públicos aos recursos PaaS (Azure SQL, Redis, Service Bus, Key Vault), roteando todo o tráfego através de Private Endpoints conectados às Private DNS Zones oficiais (`privatelink.database.windows.net`, etc.).
- **Context:** Normas do BACEN e políticas corporativas de segurança financeira proíbem estritamente a exposição de dados transacionais e bancos na internet aberta.
- **Consequences:** (+) Elimina a superfície de ataque externa contra bancos e filas; (+) Conexões utilizam FQDNs canônicos sem IP estático frágil; (-) Requer que os agentes de deploy e containers estejam obrigatoriamente dentro da VNet ou tenham rotas peering estabelecidas.

### ADR-04: Segregação de Leituras Síncronas com `ApplicationIntent=ReadOnly`
- **Decision:** Declarar formalmente na topologia do Azure SQL Database a réplica síncrona secundária habilitada no SKU General Purpose / Business Critical, orientando consultas de consulta de saldo e extrato a utilizarem `ApplicationIntent=ReadOnly`.
- **Context:** O volume de consultas de saldo disputa locks e consome IOPS da base de escrita transacional de pagamentos Pix se ambas compartilharem o nó primário.
- **Consequences:** (+) Aumenta em mais de 100% a vazão de consultas sem onerar a latência P99 das transações de pagamento; (+) Failover automático suportado pela plataforma; (-) Réplica secundária possui leve defasagem em réplicas assíncronas (embora mantida síncrona na mesma zona).
