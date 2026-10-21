# Validação e Linting de Pipelines no GitHub Actions

> Análise estática, verificação sintática, checagem de tipos de expressões e conformidade de regras em arquivos `.github/workflows/*.yml`.

---

## 1. Por que Linters de YAML Comuns são Insuficientes?

Um arquivo de workflow do GitHub Actions é escrito em YAML, mas sua semântica vai muito além da sintaxe YAML convencional:
- Expressões `${{ ... }}` possuem tipagem estrita (`boolean`, `number`, `string`, `array`, `object`).
- Contextos específicos do GitHub (`github.*`, `matrix.*`, `steps.*`, `secrets.*`, `env.*`) só estão disponíveis em determinados pontos do pipeline.
- Blocos `run:` contêm scripts de shell inteiros que podem conter erros de quoting, variáveis não declaradas e falhas de lógica.
- Ações (`uses:`) possuem contratos de entrada (`with:`) e saída (`outputs:`) que devem corresponder aos esquemas declarados.

Linters comuns (como `yamllint`) apenas garantem que a indentação e os caracteres especiais estão corretos. É fundamental empregar **validadores especializados em GitHub Actions**.

---

## 2. A Ferramenta Central: `actionlint`

* **Repositório:** [`rhysd/actionlint`](https://github.com/rhysd/actionlint)
* **Linguagem:** Go (binário estático rápido, roda em milissegundos)

O `actionlint` é o padrão da indústria para validação estática de workflows.

### Capacidades do `actionlint`:
1. **Validação de Sintaxe e Estrutura:** Cruza os arquivos YAML contra a especificação formal de schema do GitHub Actions.
2. **Checagem de Tipos em Expressões:** Analisa expressões `${{ ... }}` e valida chamadas de funções embutidas como `contains()`, `startsWith()`, `fromJson()`, alertando se uma string for passada onde um array era esperado.
3. **Inspeção de Inputs de Actions:** Baixa ou lê metadados de ações populares do GitHub Marketplace e verifica se os parâmetros em `with:` existem e são válidos.
4. **Integração com `shellcheck`:** Se o `shellcheck` estiver instalado no PATH, o `actionlint` extrai automaticamente os scripts de todos os blocos `run:` e os submete ao linter de shell, apontando erros de sintaxe bash/sh, variáveis sem aspas e comandos perigosos.
5. **Integração com `pyflakes`:** Se scripts inline em Python forem detectados, eles podem ser avaliados estaticamente.

### Executando Localmente:
```bash
# Instalação rápida via Go
go install github.com/rhysd/actionlint/cmd/actionlint@latest

# Ou via Homebrew / Scoop
brew install actionlint
scoop install actionlint

# Validar todos os workflows do repositório
actionlint
```

### Exemplo de Erro Capturado pelo `actionlint`:
```text
.github/workflows/ci.yml:25:15: property "bransh" is not defined in object type {ref: string; sha: string} [expression]
  |
25|       if: ${{ github.event.pull_request.bransh == 'main' }}
  |               ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
```

---

## 3. Conformidade Arquitetural: `ghalint`

* **Repositório:** [`suzuki-shunsuke/ghalint`](https://github.com/suzuki-shunsuke/ghalint)
* **Foco:** Práticas recomendadas e design de engenharia para workflows e composite actions.

Enquanto o `actionlint` foca em sintaxe e semântica, o `ghalint` foca em **governança e boas práticas corporativas**:

### Regras Principais do `ghalint`:
- **`job_permissions`**: Garante que nenhum job seja criado sem declarar suas permissões explicitamente.
- **`workflow_secrets`**: Evita passar segredos de forma desnecessária para reusable workflows.
- **`action_ref_should_be_full_commit_hash`**: Alerta quando ações utilizam tags mutáveis (ex.: `@v4`) em vez de commit SHAs completos.
- **`timeout_minutes_is_required`**: Obriga a declaração de `timeout-minutes` em todos os jobs para prevenir cobranças surpresa por runners travados em loop infinito.

### Instalação e Execução:
```bash
# Executar via aqua ou binário do GitHub Releases
ghalint run
```

---

## 4. Ordenação Canônica de Chaves de Mapas (Regra ORD-01)

Presente em skills avançadas de automação (como no repositório `y-miyazaki/config`), a regra **ORD-01** padroniza a organização visual e estrutural dos arquivos YAML.

### Benefícios:
- **Redução de conflitos de merge:** Quando múltiplos desenvolvedores ou agentes editam o mesmo workflow, a ordem das chaves se mantém idêntica.
- **Diffs limpos no Git:** Permite revisão de PR focada no conteúdo alterado, sem reordenação acidental de propriedades.

### Ordem Canônica Recomendada de Chaves de Job:
```yaml
jobs:
  minha-tarefa:
    name: ...           # 1. Nome legível
    runs-on: ...        # 2. Runner
    timeout-minutes: .. # 3. Timeout defensivo
    concurrency: ...    # 4. Controle de concorrência
    permissions: ...    # 5. Escopo de permissões
    environment: ...    # 6. Contexto de ambiente
    needs: ...          # 7. Dependências
    if: ...             # 8. Condicional
    strategy: ...       # 9. Matriz (se houver)
    steps: ...          # 10. Passos sequenciais
```

---

## 5. Implementando o Gate de Auto-Validação (Meta-Workflow)

Para garantir que nenhum workflow malformado ou inseguro seja integrado ao repositório, configura-se um workflow de CI dedicado a inspecionar os próprios workflows:

```yaml
name: Validate Workflows

on:
  pull_request:
    paths:
      - '.github/workflows/**'
      - '.github/actions/**'

permissions:
  contents: read

jobs:
  actionlint:
    name: Run actionlint & ghalint
    runs-on: ubuntu-latest
    steps:
      - name: Checkout Repository
        uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
        with:
          persist-credentials: false

      - name: Install Tools
        run: |
          sudo apt-get update && sudo apt-get install -y shellcheck
          bash <(curl https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash)
          sudo mv ./actionlint /usr/local/bin/

      - name: Execute actionlint
        run: actionlint -color
```

### Anotações Diretas em Pull Requests (`reviewdog/action-actionlint`):
Se preferir que os erros apareçam como comentários diretamente nas linhas do diff do Pull Request, utilize a action da comunidade:
```yaml
      - name: Actionlint com Reviewdog
        uses: reviewdog/action-actionlint@db580f4f9f6e1f0e74b3d75df07d1a2fcfe32986 # v1.58.0
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          reporter: github-pr-review
```

