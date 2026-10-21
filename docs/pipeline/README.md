# Hub de Skills e Engenharia de GitHub Actions

> Mapeamento aprofundado, catálogo de repositórios do GitHub, ferramentas de validação, segurança e especificações de **Agent Skills** prontas para criação, validação, hardening e otimização de esteiras CI/CD.

---

## 📌 Visão Geral

Este diretório consolida a pesquisa de **Skills** especializadas para **GitHub Actions** disponíveis no ecossistema do GitHub e nas comunidades de agentes autônomos de IA (Antigravity, Claude Code, OpenAI Codex, Cursor, GitHub Copilot e GitHub Next).

O termo **Skill** neste contexto engloba duas dimensões complementares:
1. **Agent Skills (`SKILL.md`)**: Pacotes portáteis de conhecimento, contexto, heurísticas e scripts que ensinam assistentes e agentes de IA a conceber, inspecionar, depurar, validar e proteger fluxos de trabalho do GitHub Actions.
2. **Ferramentas de Engenharia & Linters**: Ferramentas CLI e Actions especializadas (ex.: `actionlint`, `zizmor`, `ghalint`, `act`, `step-security`) que automatizam a verificação estática, dinâmica e de segurança de pipelines.

---

## 🧭 Mapa de Navegação da Documentação

A documentação está estruturada em tópicos modulares e aprofundados:

| Documento | Foco Principal | Destaques |
| :--- | :--- | :--- |
| [**01. Catálogo de Skills no GitHub**](./01-catalogo-skills-github.md) | Repositórios e Skills descobertas | Awesome Copilot, Claude Skills, WinDAGs, y-miyazaki config, VoltAgent, Comandos `npx skills add` |
| [**02. Criação & Scaffolding de Pipelines**](./02-criacao-e-scaffolding.md) | Geração do zero & detecção de stack | Heurísticas de detecção, geradores automáticos, Reusable Workflows (`workflow_call`), Azure OIDC |
| [**03. Validação & Linting de Workflows**](./03-validacao-e-linting.md) | Validação sintática e de boas práticas | `actionlint`, `ghalint`, ordenação ORD-01, verificação de expressões `${{ }}`, `shellcheck` inline |
| [**04. Segurança & Hardening de CI/CD**](./04-seguranca-e-hardening.md) | O modelo de ameaças do GitHub Actions | Prevenção de Script Injection, `pull_request_target` RCE, auditoria `zizmor`, SHA-pinning, Least Privilege |
| [**05. Execução Local & Testes**](./05-execucao-local-e-testes.md) | Simulação e depuração local | `nektos/act` com Docker, testes de composite actions com `bats-core`, debug interativo com `tmate` |
| [**06. Eficiência & Otimização de Custos**](./06-eficiencia-e-otimizacao.md) | Redução de minutos de CI e latência | Caching estratégico (`actions/cache`, Docker GHA), `concurrency`, filtragem de paths, poda de matrizes |
| [**07. Pipelines Agenticos (gh-aw)**](./07-pipelines-agenticos-gh-aw.md) | O futuro dos workflows com IA | GitHub Agentic Workflows (`gh-aw`), compilação de Markdown para `.lock.yml`, segurança sandboxed |
| [**08. Definição da Esteira de CI/CD**](./08-definicao-esteira-cicd-completa.md) | **Especificação Completa & Revisão Codex** | Diagnóstico dos gaps de `.github/`, comandos reais Azure CLI, Blue/Green, Canary 10%, Cosign, Syft, Trivy e parecer Codex |

---

## 🎯 Especificação da Esteira de Produção

A definição completa e acionável da esteira de CI/CD para o projeto **Plataforma Pix** está documentada em [**`08-definicao-esteira-cicd-completa.md`**](./08-definicao-esteira-cicd-completa.md).
Ela substitui os comandos mockados (`Write-Host`) anteriormente presentes em `.github/workflows/` por chamadas reais à CLI do Azure (`az containerapp`), incorpora polling síncrono para o job do `Pix.Migrator` e estabelece gates de aprovação e circuit breaker com Dynatrace Davis AI.

---

## 🛠️ Matriz de Ferramentas Essenciais do Ecossistema

| Ferramenta | Mantenedor / Repositório | Objetivo | Instalação Rápida |
| :--- | :--- | :--- | :--- |
| **actionlint** | [`rhysd/actionlint`](https://github.com/rhysd/actionlint) | Linter estático de sintaxe, tipos de expressões e shellcheck inline | `go install github.com/rhysd/actionlint/cmd/actionlint@latest` ou brew/scoop |
| **zizmor** | [`zizmorcore/zizmor`](https://github.com/zizmorcore/zizmor) | Scanner estático de vulnerabilidades de segurança específicas de Actions | `pip install zizmor` ou `cargo install zizmor` |
| **ghalint** | [`suzuki-shunsuke/ghalint`](https://github.com/suzuki-shunsuke/ghalint) | Linter de conformidade arquitetural e boas práticas de workflows | `aqua g -i suzuki-shunsuke/ghalint` ou binário GitHub |
| **act** | [`nektos/act`](https://github.com/nektos/act) | Execução local de workflows usando containers Docker | `brew install act` ou `choco install act-cli` |
| **harden-runner**| [`step-security/harden-runner`](https://github.com/step-security/harden-runner)| Filtragem de tráfego de rede e integridade de arquivos em tempo de execução | Step no workflow YAML |
| **gh-aw** | [`github/gh-aw`](https://github.com/github/gh-aw) | GitHub Agentic Workflows CLI (compilador Markdown -> Actions) | `gh extension install github/gh-aw` |

