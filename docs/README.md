# Project Documentation

Index of project technical documentation for **Modernização da Plataforma Pix**. Use the links below to navigate the available architecture decisions and feature guides.

## Documentation Index

| Document | Description | Reading |
|----------|-------------|----------|
| [**.digest.md**](./.digest.md) | Fast-path machine-readable orientation digest (stack, test commands, rules). | **Mandatory** |
| [**.graph.json**](./.graph.json) | Macro relation graph index for agent topology navigation and 1-hop routing. | **Mandatory** |
| [**ARCHITECTURE.md**](./adr/ARCHITECTURE.md) | Hexagonal architecture, Zero Trust network, YARP Gateway, persistence, outbox, and observability patterns. | **Mandatory** |
| [**TESTS.md**](./adr/TESTS.md) | Testing strategies, patterns, execution commands, gateway test scenarios, and coverage thresholds. | **Mandatory** |
| [**infra_landing_zone.md**](./feature/infra_landing_zone.md) | Landing Zone Enterprise no Azure (Bicep, VNet, NSG, Private Endpoints) e API Gateway YARP com propagação W3C/OIDC. | **Mandatory** |
| [**plataforma_pix.md**](./feature/plataforma_pix.md) | Core Pix platform domain capabilities, contracts, rules, and execution flow. | **Mandatory** |
| [**qa_pipeline.md**](./feature/qa_pipeline.md) | Continuous testing pyramid, quality pipeline, and perimetral auth verification. | **Mandatory** |
| [**cicd_pipeline.md**](./feature/cicd_pipeline.md) | Esteira de CI/CD automatizada, gates manuais para Hml/Prod, proveniência Cosign e rollback determinístico. | **Mandatory** |
| [**observabilidade_dynatrace.md**](./feature/observabilidade_dynatrace.md) | Observabilidade de produção, propagação W3C, sanitização de PII e quality gates com Dynatrace SRG e Davis AI. | **Mandatory** |
| [**pbb_cicd_pipeline.md**](./feature/pbb_cicd_pipeline.md) | Product Backlog Building (PBB) da esteira de CI/CD e entrega contínua. | **Mandatory** |
| [**pbb_infra_landing_zone.md**](./feature/pbb_infra_landing_zone.md) | Product Backlog Building (PBB) da infraestrutura cloud-native, borda e Landing Zone Enterprise. | **Mandatory** |
| [**CICD.md**](./adr/CICD.md) | Proposed GitHub Actions release strategy and acceptance criteria. | Optional |
| [**CONTAINER-APPS.md**](./adr/CONTAINER-APPS.md) | Proposed Azure runtime and scaling policy. | Optional |

## Modernization & Engineering Engine (Fractal Methodology)

| Document | Description | Target |
|----------|-------------|--------|
| [**BLUEPRINT.md**](./engineering/BLUEPRINT.md) | Blueprint Canônico: Ciclo 0, cadência fractal em 4 tempos e regras de camadas. | **Mandatory for Agents** |
| [**LIFECYCLE.md**](./engineering/LIFECYCLE.md) | Máquina de estados da fatia, gates de transição e schema de handoff.json. | **Mandatory for Agents** |
| [**ORACLE.md**](./engineering/ORACLE.md) | Protocolo de caracterização do legado, fixtures e oráculo de paridade. | **Mandatory for Agents** |
| [**WALKING-SKELETON.md**](./engineering/WALKING-SKELETON.md) | Guia de inicialização do Ciclo 0 (/health + Dynatrace + CI/CD isolado). | **Mandatory for Agents** |
| [**AGENT-CONTRACTS.md**](./engineering/AGENT-CONTRACTS.md) | Matriz dos 6 papéis de agentes, escopos de arquivos e limites de atuação. | **Mandatory for Agents** |
| [**MIGRATION.md**](./engineering/MIGRATION.md) | Protocolo de autoridade de escrita, corte no YARP e runbooks de rollback. | **Mandatory for Cutover** |

## Recommended Reading Order

If an exact path is supplied, read it directly. Otherwise use this order:

1. **.digest.md** — fast AI orientation (architecture pattern, stack, test commands).
2. **.graph.json** — macro relation graph index for 1-hop document lookup.
3. Selected ADR or feature documents only when their design context is required.
