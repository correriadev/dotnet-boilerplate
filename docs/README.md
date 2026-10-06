# Project Documentation

Index of project technical documentation for **Modernização da Plataforma Pix**. Use the links below to navigate the available architecture decisions and feature guides.

## Documentation Index

| Document | Description | Reading |
|----------|-------------|----------|
| [**ARCHITECTURE.md**](./adr/ARCHITECTURE.md) | Hexagonal architecture, persistence, outbox, and messaging patterns. | **Mandatory** |
| [**TESTS.md**](./adr/TESTS.md) | Testing strategies, patterns, and execution commands. | **Mandatory** |
| [**plataforma_pix.md**](./feature/plataforma_pix.md) | Core Pix platform domain capabilities, contracts, rules, and execution flow. | **Mandatory** |
| [**qa_pipeline.md**](./feature/qa_pipeline.md) | Continuous testing pyramid, quality pipeline, and perimetral auth verification. | **Mandatory** |
| [**cicd_pipeline.md**](./feature/cicd_pipeline.md) | Esteira de CI/CD automatizada, gates manuais para Hml/Prod, proveniência Cosign e rollback determinístico. | **Mandatory** |
| [**pbb_cicd_pipeline.md**](./feature/pbb_cicd_pipeline.md) | Product Backlog Building (PBB) da esteira de CI/CD e entrega contínua. | **Mandatory** |
| [**pbb_infra_landing_zone.md**](./feature/pbb_infra_landing_zone.md) | Product Backlog Building (PBB) da infraestrutura cloud-native, borda e Landing Zone Enterprise. | **Mandatory** |
| [**CICD.md**](./adr/CICD.md) | Proposed GitHub Actions release strategy and acceptance criteria. | Optional |
| [**CONTAINER-APPS.md**](./adr/CONTAINER-APPS.md) | Proposed Azure runtime and scaling policy. | Optional |

## Domain Specifications (DDD Specs)

Especificações técnicas derivadas do refinamento de escopo (`/scope-refinement`) localizadas em `docs/specs/`:

* [**001-problem-space.md**](./specs/cicd_pipeline/001-problem-space.md) — Event Storming, subdomínios e linguagem ubíqua do CI/CD.
* [**002-context-map.md**](./specs/cicd_pipeline/002-context-map.md) — Bounded Contexts, mapa de relações e decisões arquiteturais (ADR-SR-01 a 06).
* [**003-azure-cloud-estudos-tactical-design.md**](./specs/cicd_pipeline/003-azure-cloud-estudos-tactical-design.md) — Agregados, VOs, Domain Services e plano ordenado de tarefas.
* [**004-azure-cloud-estudos-test-scenarios.md**](./specs/cicd_pipeline/004-azure-cloud-estudos-test-scenarios.md) — Especificações de testes em formato Given-When-Then.

## Recommended Reading Order

If an exact path is supplied, read it directly. Otherwise use this order:

1. **.digest.md** — fast AI orientation (architecture pattern, stack, test commands).
2. **.graph.json** — macro relation graph index for 1-hop document lookup.
3. Selected ADR or feature documents only when their design context is required.
