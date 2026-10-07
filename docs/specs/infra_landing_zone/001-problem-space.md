# Strategic Design — Problem Space (infra_landing_zone)

## Section 1 — Big Picture Event Storming

Fluxo temporal ordenado cobrindo o ciclo de vida de provisionamento, isolamento perimetral, roteamento no gateway, resolução de DNS privado e alta disponibilidade com réplica síncrona:

| # | Domain Event (past tense) | Command (trigger) | Aggregate | External Systems | Read Models |
|---|---|---|---|---|---|
| 01 | LandingZoneProvisioningInitiated | IniciarProvisionamentoLandingZone | LandingZoneTopology | Azure Resource Manager | VisaoGeralLandingZone |
| 02 | VirtualNetworkAndSubnetsCreated | CriarRedeVirtualESubnets | TopologiaRede | Azure VNet / NSG | MapaSubnets |
| 03 | PrivateDnsZonesLinked | VincularZonasDnsPrivadas | ResolucaoDnsPrivado | Azure Private DNS | CatalogoZonasDns |
| 04 | KeyVaultCustodyConfigured | ConfigurarCustodiaKeyVault | CustodiaSegredos | Azure Key Vault / Managed Identity | InventarioSegredos |
| 05 | PrivateEndpointsEstablished | EstabelecerPrivateEndpoints | PontoAcessoPrivado | Azure Private Link | CatalogoPrivateEndpoints |
| 06 | SqlDatabaseWithSyncReplicaProvisioned | ProvisionarBancoComReplicaSincrona | TopologiaDados | Azure SQL Server / HADR | TopologiaBancoDados |
| 07 | IngressGatewayConfigured | ConfigurarGatewayBorda | RoteamentoGateway | YARP / Cloudflare | RoteamentoGatewayView |
| 08 | EdgeSecurityPoliciesEnforced | AplicarPoliticasSegurancaBorda | PoliticaBorda | Cloudflare WAF / YARP | PainelPoliticasBorda |
| 09 | RequestRoutedViaGateway | RotearRequisicaoGateway | ProxyReversoYarp | Cliente HTTP Externo | MetricasTrafegoGateway |
| 10 | SecurityHeadersForwarded | RepassarCabecalhosSeguranca | ProxyReversoYarp | Pix.CashOut.Api / Pix.Payment.Api | AuditoriaRequisicoes |
| 11 | ReadOnlyQueryRoutedToSyncReplica | RotearConsultaReplicaLeitura | RoteadorLeituraSql | Azure SQL Secondary Replica | RelatorioCargaReplicas |
| 12 | SecretRetrievedViaManagedIdentity | ObterSegredoViaManagedIdentity | CustodiaSegredos | Azure Key Vault CSI | AuditoriaAcessosKeyVault |
| 13 | LocalLabParitySynchronized | SincronizarParidadeLaboratorio | AmbienteLocalCompose | Docker Compose Engine | EstadoLaboratorioLocal |
| 14 | InfrastructureHealthAudited | AuditarSaudeInfraestrutura | AuditoriaInfraestrutura | Azure Monitor / OpenTelemetry | DashboardSaudeInfra |

---

## Section 2 — Subdomain Classification

| Subdomain | Type | Justification |
|---|---|---|
| **Roteamento e Borda Perimetral (Gateway & Edge)** | Core | Diferencial competitivo para a plataforma Pix: unifica a fachada pública, intercepta e valida cabeçalhos W3C/OIDC e protege os serviços internos com isolamento Zero Trust. |
| **Isolamento de Rede e Conectividade Privada (VNet & Private Link)** | Supporting | Viabiliza a segurança e conformidade BACEN ao eliminar IPs públicos de bancos e mensageria, sustentando o Core Domain. |
| **Alta Disponibilidade e Segregação de Dados (HADR & Read-Replica)** | Supporting | Permite escalabilidade de leitura e garantia de continuidade de negócio via réplica secundária síncrona do Azure SQL. |
| **Custódia de Segredos e Identidade (Key Vault & RBAC)** | Generic | Gestão commodity de credenciais, certificados mTLS e chaves criptográficas provida nativamente pelo Azure Key Vault. |
| **Paridade e Emulação Local (Docker Compose Lab)** | Supporting | Assegura que o ambiente de desenvolvimento local reproduza com fidelidade a topologia de nuvem sem acoplar custos ou dependências externas. |

---

## Section 3 — Ubiquitous Language Glossary

| Term | Definition | Notes |
|---|---|---|
| **Landing Zone** | Ambiente estruturado em nuvem projetado com governança, isolamento de rede, controle de acesso e conformidade de segurança para hospedar cargas de trabalho corporativas. | Não confundir com uma simples Subscription ou Resource Group isolado; envolve topologia de rede multi-camadas e políticas centralizadas. |
| **Private Endpoint** | Interface de rede privada que conecta de forma segura e exclusiva uma aplicação a um serviço PaaS do Azure através de um IP privado da VNet. | Elimina necessidade de Service Endpoints públicos ou regras de IP de saída. |
| **Private DNS Zone** | Zona de resolução de nomes privativa no Azure que mapeia FQDNs de serviços PaaS (como `database.windows.net`) diretamente para seus respectivos IPs de Private Endpoints dentro da VNet. | Evita o antipadrão de conexões via IP estático hardcoded em connection strings. |
| **YARP (Yet Another Reverse Proxy)** | Biblioteca de alta performance em .NET 8 utilizada como API Gateway reverso local e fachada de microsserviços, roteando tráfego HTTP e preservando contexto de segurança. | Não é um simples proxy HTTP passivo; orquestra rotas, clusters e propagação transversal de cabeçalhos. |
| **Borda Híbrida (Hybrid Edge)** | Arquitetura de borda que emprega Cloudflare (WAF/DDoS e terminação TLS) nos ambientes de nuvem e YARP como gateway interno e emulador local no Docker Compose. | Garante paridade de funcionalidade sem dependência de internet ou custos no desenvolvimento local. |
| **Réplica de Leitura Síncrona** | Nó secundário do Azure SQL Database mantido em sincronia imediata com o primário, acessado exclusivamente através da flag de conexão `ApplicationIntent=ReadOnly`. | Utilizado para consultas de saldo e relatórios sem disputa de concorrência com escritas transacionais de pagamentos. |
| **Secrets Store CSI Driver** | Mecanismo que monta segredos do Azure Key Vault diretamente como arquivos no sistema de arquivos dos containers via Managed Identity, sem persistência em variáveis de ambiente. | Evita exposição de credenciais em dumps de memória de ambiente ou logs de orquestração. |
| **Subnet Segregation** | Divisão lógica da VNet em 3 subredes isoladas (`snet-gateway`, `snet-apps`, `snet-data`) protegidas por Network Security Groups (NSGs). | Garante o princípio de menor privilégio onde apenas `snet-apps` tem acesso à `snet-data`. |
| **W3C Trace Forwarding** | Ação do gateway de extrair, validar e repassar os cabeçalhos `traceparent` e `tracestate` das requisições externas para os microsserviços internos. | Preserva o identificador único de rastreamento do cliente até a liquidação no SPI do BACEN. |
| **Managed Identity (User-Assigned)** | Identidade gerenciada do Azure Active Directory associada aos microsserviços para autenticação em recursos do Azure sem necessidade de senhas ou tokens fixos. | Elimina o risco de vazamento de credenciais em arquivos de configuração ou repositórios de código. |
| **Infrastructure as Code (IaC) Modular** | Definição declarativa da infraestrutura em arquivos Bicep reutilizáveis, versionados e orquestrados por um arquivo raiz `main.bicep`. | Substitui scripts imperativos manuais (CLI/PowerShell) por provisionamento idempotente. |
| **Fail-Closed Edge Security** | Política perimetral que rejeita sumariamente qualquer tráfego que não apresente autenticação válida ou viole regras do WAF, impedindo a sobrecarga dos microsserviços. | Protege a capacidade transacional da plataforma Pix contra acessos indevidos na primeira milha. |
