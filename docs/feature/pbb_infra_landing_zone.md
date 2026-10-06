---
doc_type: feature
domain: infrastructure
stack: [Azure, Bicep, VNet, Cloudflare, YARP, Azure Key Vault, Azure SQL, OpenTelemetry, Redis, Service Bus]
node_id: "feature:pbb-infra-landing-zone"
tags: [pbb, infrastructure, landing-zone, cloudflare, yarp, key-vault, opentelemetry, bicep]
edges:
  - relation: implements
    target: "adr:architecture"
    read: must
  - relation: references
    target: "adr:container-apps"
    read: should
  - relation: depends_on
    target: "feature:pbb-cicd-pipeline"
    read: should
updated: 2026-10-06
---

# PBB 2 — Infraestrutura Cloud-Native & Landing Zone Enterprise

Backlog estruturado segundo a metodologia Product Backlog Building (PBB) para o provisionamento de infraestrutura em nuvem, borda, isolamento de rede e observabilidade da Plataforma Pix.

## 1. Product
* **Nome do Produto:** Plataforma Cloud-Native & Landing Zone Enterprise (Infraestrutura, Borda e Observabilidade Pix).
* **Objetivo:** Projetar, declarar em Infraestrutura como Código (Bicep) e emular localmente uma arquitetura de nuvem corporativa no Azure segura, isolada em rede privada, observável e em alta disponibilidade, espelhando os padrões do BACEN e do mercado financeiro para sustentar os 5 microsserviços da Plataforma Pix.
* **Fronteiras e Escopo:** Abrange o provisionamento da Landing Zone no Azure (VNet, Subnets, Private Endpoints, NSGs), a camada de borda híbrida (Cloudflare na nuvem e YARP no ambiente local), a custódia centralizada de segredos e certificados (Azure Key Vault), a instrumentação de telemetria unificada (OpenTelemetry) e a topologia de banco com réplica de leitura síncrona. Não engloba a implementação de regras de negócio internas do domínio Pix nem os scripts de esteira do GitHub Actions (já cobertos no PBB 1).

## 2. Problems
* **P01 — Exposição indevida de recursos de dados críticos na internet pública:** Bancos de dados, filas e caches operando sem isolamento de rede privada aumentam a superfície de ataque e violam as diretrizes de segurança corporativa e normas regulatórias do BACEN.
* **P02 — Múltiplos pontos de entrada desprotegidos e acoplados:** As APIs expõem portas diretamente sem uma camada unificada de gateway, impossibilitando políticas centrais de terminação TLS, roteamento unificado e proteção perimetral contra DDoS e WAF.
* **P03 — Cegueira operacional em transações financeiras distribuídas:** Em um fluxo assíncrono ($\text{API} \to \text{Outbox} \to \text{Service Bus} \to \text{Worker Outbank} \to \text{SPI Bacen}$), erros pontuais ou gargalos de latência são difíceis de diagnosticar sem correlação de contexto W3C ponta a ponta.
* **P04 — Sobrecarga na base primária de escrita por consultas pesadas:** Requisições frequentes de saldo e conciliação disputam locks diretamente com a escrita transacional de pagamentos, degradando a performance em picos de carga.
* **P05 — Disparidade entre o ambiente de desenvolvimento local e a nuvem corporativa:** Desenvolvedores testando sem gateway e com credenciais em texto plano criam riscos de inconsistência entre o comportamento local e o runtime produtivo no Azure.

## 3. Expectations
* **E01 — Isolamento estrito de rede e princípio Zero Trust (Landing Zone):** Todo o tráfego entre aplicações e recursos PaaS (Azure SQL, Redis, Service Bus, Key Vault) deve ocorrer exclusivamente através de subnets dedicadas e Private Endpoints sem IP público.
* **E02 — Ponto único de entrada e borda protegida híbrida (Cloudflare + YARP):** Proteger as entradas de nuvem com Cloudflare (Camada 7, WAF e terminação TLS) e utilizar um API Gateway em YARP (.NET 8) para roteamento interno e emulação local de testes no Docker Compose.
* **E03 — Observabilidade e rastreabilidade distribuída de ponta a ponta (OpenTelemetry):** Cada transação Pix deve carregar um cabeçalho W3C (`traceparent`) que correlacione chamadas HTTP, comandos Dapper, operações Redis e mensagens do Service Bus.
* **E04 — Segregação de carga com réplica de leitura síncrona formalizada:** Declaração explícita de infraestrutura que viabilize consultas de saldo com `ApplicationIntent=ReadOnly` na réplica secundária do Azure SQL sem impacto no primário.
* **E05 — Paridade local e modularidade de infraestrutura como código (IaC):** Módulos Bicep versionados e reprodutíveis para a nuvem, acompanhados de uma configuração equivalente no Docker Compose para desenvolvimento sem custos.

## 4. Personas
* **Persona 1: Engenheiro de Plataforma / Cloud Architect**
  * *Objetivo:* Desenhar e modularizar a infraestrutura como código (IaC), garantindo isolamento de rede, landing zones seguras e conformidade com as diretrizes corporativas.
  * *Interações:* Desenvolve módulos Bicep, gerencia topologia de VNets e provisiona recursos no Azure.
* **Persona 2: Engenheiro de Segurança / SecOps**
  * *Objetivo:* Garantir o perímetro Zero Trust, Private Endpoints, RBAC por Managed Identity, proteção de borda na Cloudflare e custódia de certificados digitais mTLS no Key Vault.
  * *Interações:* Audita regras de firewall, configurações de WAF, permissões de IAM e rotação de chaves.
* **Persona 3: Engenheiro SRE / Observabilidade**
  * *Objetivo:* Ter visibilidade imediata de gargalos, dependências em cascata e métricas de latência P99 através de dashboards e traces distribuídos.
  * *Interações:* Configura coletores OpenTelemetry, alertas no Azure Monitor e integrações APM.
* **Persona 4: Desenvolvedor Pix**
  * *Objetivo:* Executar o ecossistema com fidelidade arquitetural em sua máquina local sem depender de conexões externas ou custos de nuvem.
  * *Interações:* Utiliza o `docker-compose.yml` atualizado com YARP e testa fluxos através do gateway local.

## 5. Functionalities
* **F01 — Provisionar Landing Zone e Isolamento de Rede Privada (VNet & Private Endpoints)**
* **F02 — Implementar Camada de Borda e API Gateway Híbrido (Cloudflare & YARP Facade)**
* **F03 — Centralizar e Proteger Segredos e Certificados (Key Vault & HSM Management)**
* **F04 — Instrumentar Observabilidade e Tracing Distribuído (OpenTelemetry & Telemetry)**
* **F05 — Provisionar Dados e Alta Disponibilidade com Réplica Síncrona (Resilient Data Store)**
* **F06 — Fornecer Paridade Local e Emulação de Infraestrutura (Local Lab Parity)**

## 6. Product Backlog
* **PBI01 (F01):** Como Engenheiro de Plataforma, quero módulos Bicep que provisionem a VNet com 3 subnets dedicadas (`snet-gateway`, `snet-apps`, `snet-data`) e Network Security Groups (NSGs) para isolar o tráfego entre camadas.
* **PBI02 (F01):** Como Engenheiro de Segurança, quero configurar Private Endpoints para o Azure SQL, Redis e Service Bus para eliminar completamente o tráfego público para serviços de dados.
* **PBI03 (F02):** Como Engenheiro de Plataforma, quero configurar a Cloudflare na borda dos ambientes de nuvem (Dev/Prod) com terminação TLS, WAF e proteção DDoS Camada 7.
* **PBI04 (F02):** Como Engenheiro de Plataforma, quero implementar um projeto de API Gateway baseado em YARP (.NET 8) para centralizar a exposição das APIs de CashOut e Billing sob uma fachada unificada no ambiente de desenvolvimento local.
* **PBI05 (F02):** Como Desenvolvedor Pix, quero que o API Gateway repasse os cabeçalhos de autenticação OIDC (Bearer JWT) e de rastreamento W3C para os serviços internos para preservar a validação perimetral e o contexto transacional.
* **PBI06 (F03):** Como Engenheiro de Segurança, quero módulos Bicep para provisionar o Azure Key Vault com RBAC estrito e suporte ao Secrets Store CSI Driver para montagem transparente de credenciais nos containers via Managed Identity.
* **PBI07 (F03):** Como Engenheiro de Plataforma, quero armazenar certificados mTLS ICP-Brasil de forma segura no Key Vault para viabilizar a liquidação com o SPI do BACEN.
* **PBI08 (F04):** Como Engenheiro SRE, quero instrumentar os 5 microsserviços com o SDK do OpenTelemetry para propagar o W3C Trace Context entre requisições HTTP, queries Dapper, operações Redis e filas do Service Bus.
* **PBI09 (F04):** Como Engenheiro SRE, quero exportar métricas e traces para o Azure Log Analytics e Application Insights (e Aspire Dashboard / Jaeger no ambiente local) para visualizar o caminho crítico das transações.
* **PBI10 (F05):** Como DBA, quero declarar via Bicep a configuração do Azure SQL Database General Purpose com réplica secundária síncrona habilitada para suportar leituras de saldo com `ApplicationIntent=ReadOnly`.
* **PBI11 (F06):** Como Desenvolvedor Pix, quero atualizar o `docker-compose.yml` para incluir o container do YARP Gateway e um coletor de telemetria para testar o ecossistema localmente com paridade total em relação à nuvem.

## 7. Traceability

| Problema / Expectativa | Persona | Funcionalidade | PBI |
| :--- | :--- | :--- | :--- |
| P01 / E01 (Isolamento / Landing Zone) | Engenheiro de Plataforma / Segurança | F01 (VNet & Private Endpoints) | PBI01, PBI02 |
| P02 / E02 (Borda unificada e Gateway) | Engenheiro de Plataforma / Dev | F02 (Cloudflare & YARP Facade) | PBI03, PBI04, PBI05 |
| P01, P05 / E01, E05 (Key Vault e Certificados) | Engenheiro de Segurança / Plataforma | F03 (Key Vault & HSM) | PBI06, PBI07 |
| P03 / E03 (Tracing e Observabilidade) | Engenheiro SRE | F04 (OpenTelemetry & Telemetry) | PBI08, PBI09 |
| P04 / E04 (Sobrecarga de banco / Réplica) | DBA | F05 (Resilient Data Store) | PBI10 |
| P05 / E05 (Paridade local / Simulação) | Desenvolvedor Pix | F06 (Local Lab Parity) | PBI11 |

## 8. Assumptions
* No ambiente de laboratório pessoal, o Azure Key Vault utilizará SKU Standard por otimização de custo, mantendo exatamente os mesmos contratos e comportamento de Managed Identity do SKU Premium HSM corporativo.
* A borda dos ambientes de nuvem utilizará o plano gratuito da Cloudflare (com DNS autoritativo, SSL automático na borda e regras de firewall), enquanto o ambiente local de desenvolvimento utilizará o YARP (.NET 8) no Docker Compose sem dependência de internet.
* A instrumentação de telemetria utilizará exclusivamente bibliotecas padrão do OpenTelemetry (.NET), evitando lock-in e permitindo envio tanto para o Application Insights quanto para o Dynatrace.

## 9. Open Questions

### Q01 — Qual estratégia de API Gateway e Borda deve ser adotada para simular a borda perimetral nos ambientes do laboratório e no teste local?

**Suggested answer:** Utilizar uma estratégia híbrida: YARP (.NET 8) como API Gateway interno e emulador local no Docker Compose, e Cloudflare na borda externa dos ambientes em nuvem.

**Rationale:** Combina a proteção de borda, terminação TLS e WAF gratuitos da Cloudflare nos ambientes remotos com a flexibilidade e zero-dependência de internet do YARP no desenvolvimento local no Docker Compose.

**Resolved answer:** Adotar **YARP (.NET 8)** como API Gateway reverso para o ambiente local de testes (Docker Compose) e **Cloudflare** (Camada 7, terminação TLS e WAF na borda) para os ambientes de nuvem (Dev e Produção).

**Answered by:** human

**Status:** Human validated
