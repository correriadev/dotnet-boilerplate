# Catálogo de Skills e Repositórios no GitHub para GitHub Actions

> Mapeamento minucioso dos principais repositórios no GitHub que disponibilizam **Agent Skills**, regras de instrução para agentes e ferramentas para concepção e validação de pipelines CI/CD.

---

## 1. Repositórios de Destaque no GitHub

### 1.1 `github/awesome-copilot`
* **Repositório:** [https://github.com/github/awesome-copilot](https://github.com/github/awesome-copilot)
* **Mantenedor:** GitHub Community / GitHub Staff
* **Descrição:** A coleção oficial da comunidade do GitHub com instruções, agentes customizados, extensões e skills especializadas.
* **Componentes Chave para GitHub Actions:**
  * **`skills/github-actions-hardening/SKILL.md`**: Foco absoluto no modelo de ameaças específico do GitHub Actions. Analisa injeção de script em `${{ }}`, escalada de privilégios com `pull_request_target`, unpinned actions e permissões excessivas no `GITHUB_TOKEN`.
  * **`skills/github-actions-efficiency/SKILL.md`**: Foco em auditoria de desempenho de CI, identificação de gargalos de tempo de execução, redução de matrizes desnecessárias, configuração de `concurrency` e políticas de cache.
  * **`agents/github-actions-expert.agent.md`**: Persona especializada para Copilot e outros agentes, definindo um checklist rigoroso antes de gerar qualquer workflow.
  * **`instructions/github-actions-ci-cd-best-practices.instructions.md`**: Guia abrangente de boas práticas de engenharia de software para CI/CD (jobs, outputs, triggers granulares, tolerância a falhas).

### 1.2 `alirezarezvani/claude-skills`
* **Repositório:** [https://github.com/alirezarezvani/claude-skills](https://github.com/alirezarezvani/claude-skills)
* **Mantenedor:** Alireza Rezvani
* **Descrição:** Um dos repositórios mais completos e estruturados para Claude Code, Antigravity e agentes baseados em arquivos `SKILL.md`.
* **Componente Chave:**
  * **`engineering/skills/ci-cd-pipeline-builder/SKILL.md`**:
    * Autodetecção de stack baseada nos arquivos do repositório (`scripts/stack_detector.py`).
    * Gerador de pipeline com suporte a GitHub Actions e GitLab CI (`scripts/pipeline_generator.py`).
    * Estratégias automáticas de cache e matrizes de teste baseadas nos lockfiles (`package-lock.json`, `poetry.lock`, `Cargo.lock`, `go.sum`, etc.).
  * **Instalação rápida:**
    ```bash
    npx -y skills add alirezarezvani/claude-skills --skill ci-cd-pipeline-builder
    ```

### 1.3 `curiositech/windags-skills`
* **Repositório:** [https://github.com/curiositech/windags-skills](https://github.com/curiositech/windags-skills)
* **Mantenedor:** CuriosiTech
* **Descrição:** Mais de 190 skills portáteis para agentes de IA (Claude Code, Cursor, Codex, Gemini CLI).
* **Componente Chave:**
  * **`skills/github-actions-pipeline-builder/SKILL.md`**:
    * Guia avançado de anti-patterns de CI/CD (ex.: compilações lentas sem cache, duplicação de YAML sem matrizes, segredos expostos em código).
    * Árvores de decisão rápidas para escolha de tecnologia e tipos de runners.
    * Padrões prontos para: CI multi-ambiente, releases automatizadas com `softprops/action-gh-release`, e builds Docker com cache via `type=gha`.
  * **Instalação rápida:**
    ```bash
    npx -y skills add curiositech/windags-skills --skill github-actions-pipeline-builder
    ```

### 1.4 `y-miyazaki/config`
* **Repositório:** [https://github.com/y-miyazaki/config](https://github.com/y-miyazaki/config)
* **Mantenedor:** Yoshiyuki Miyazaki
* **Descrição:** Coleção refinada de automações e skills para engenharia de software e validação de repositórios.
* **Componentes Chave:**
  * **`.agents/skills/github-actions-validation/SKILL.md`**:
    * Uma das mais rigorosas pipelines de validação estática de workflows existentes.
    * Orquestração unificada através de script (`scripts/validate.sh`) que roda sequencialmente:
      1. Verificação de ordenação canônica de chaves de mapas YAML (**ORD-01**).
      2. **`actionlint`** (com shellcheck integrado).
      3. **`ghalint`** (boas práticas arquiteturais).
      4. **`zizmor`** (auditoria de segurança SAST, incluindo suporte a modo offline).
  * **`.agents/skills/ci-sweeper/SKILL.md`**:
    * Limpeza periódica de artefatos obsoletos, workflows cancelados e branches de CI.

### 1.5 `VoltAgent/awesome-agent-skills` & Ecossistema `skills.sh`
* **Repositório:** [https://github.com/VoltAgent/awesome-agent-skills](https://github.com/VoltAgent/awesome-agent-skills)
* **Diretório:** [https://skillsdirectory.com](https://skillsdirectory.com) / [https://skills.rest](https://skills.rest)
* **Descrição:** Catálogo curado com mais de 1.000 agent skills compatíveis com Claude Code, Codex, Antigravity e Cursor.
* **Skills Relevantes Mapeadas:**
  * **`testmu-ai/cicd-pipeline-skill`**: Criação de suítes de testes contínuos integradas com GitHub Actions, Jenkins e Azure DevOps.
  * **`github-actions-to-gitlab-ci`**: Transpilação e conversão automatizada de arquivos `.github/workflows/*.yml` para `.gitlab-ci.yml`.
  * **`cwinvestments/memstack` (`ci-cd-pipeline`)**: Geração automática de pipelines baseada em memória de projetos.
  * **`Resgrid/Core` (`ci-cd`)**: Foco em pipelines híbridos (.NET, Azure DevOps e GitHub Actions).

### 1.6 `github/gh-aw` (GitHub Agentic Workflows)
* **Repositório:** [https://github.com/github/gh-aw](https://github.com/github/gh-aw)
* **Criadores:** GitHub Next / Microsoft Research / Azure Core Upstream
* **Descrição:** O mais novo paradigma de automação onde workflows do GitHub Actions são descritos em linguagem natural e Markdown (`.github/workflows/*.md`) e compilados para `.lock.yml`.
* **Destaques:**
  * Permite que agentes (Copilot, Claude, Codex, Gemini) executem automações de repositório (ex.: triagem de issues, revisão de PRs, correção de falhas de CI).
  * Execução em sandbox seguro, permissões estritamente read-only por padrão e sanitização de "Safe Outputs".
  * Suporte nativo ao Model Context Protocol (MCP) para interagir com o ecossistema GitHub.

---

## 2. Padrões de Estruturação de uma Agent Skill

Analisando os melhores repositórios do GitHub, identifica-se que uma **Skill de Alto Nível** para GitHub Actions é composta por:

```
minha-skill-github-actions/
├── SKILL.md                 # Arquivo central (frontmatter YAML + instruções procedurais)
├── scripts/                 # Scripts executáveis pelo agente ou localmente
│   ├── detect_stack.py      # Identifica linguagem, gerenciador de pacotes e comandos
│   └── validate.sh          # Executa actionlint + zizmor + ghalint em cadeia
├── references/              # Conhecimento especializado sob demanda (Progressive Disclosure)
│   ├── threat-model.md      # Casos de injeção de script e trust boundaries
│   ├── oidc-azure.md        # Receitas de autenticação sem segredos
│   └── performance.md       # Regras de concorrência e chaves de cache
└── assets/ ou templates/    # Modelos canônicos de workflow prontos para customização
    ├── ci-pr.yml
    └── cd-deploy.yml
```

### O Conceito de "Progressive Disclosure"
Agentes de IA possuem limites de janela de contexto. As skills mais bem-sucedidas no GitHub não sobrecarregam o prompt inicial. Elas mantêm um `SKILL.md` enxuto com pontos de decisão rápidos e apontam links relativos para `references/*.md` somente quando uma situação específica é ativada (ex.: autenticação na Azure via OIDC ou depuração de um erro específico do `actionlint`).

---

## 3. Como Importar e Usar essas Skills

### Via Gerenciador de Pacotes `skills` (Vercel)
O utilitário `npx skills` permite baixar skills de qualquer repositório GitHub público diretamente:

```bash
# Adicionar a skill de criação de pipeline
npx -y skills add alirezarezvani/claude-skills --skill ci-cd-pipeline-builder

# Adicionar a skill de boas práticas e anti-patterns
npx -y skills add curiositech/windags-skills --skill github-actions-pipeline-builder
```

### Via Clonagem Direta no Projeto
Para uso no Antigravity, Claude Code ou Codex:
```bash
# Clonar como submódulo ou pasta de referência
git submodule add https://github.com/github/awesome-copilot.git .agents/awesome-copilot
```
Ou copiar os arquivos `.md` das skills para `.gemini/antigravity/skills/` ou `.agents/skills/`.

