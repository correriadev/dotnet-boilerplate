# Eficiência e Otimização de Custos em GitHub Actions

> Práticas e técnicas auditadas para reduzir o tempo de execução (wall-clock time), eliminar desperdício de minutos faturados e acelerar o ciclo de feedback dos desenvolvedores.

---

## 1. O Custo Oculto da Ineficiência em CI/CD

Esteiras lentas e mal otimizadas geram dois problemas críticos:
1. **Desperdício Financeiro:** Runners GitHub-hosted cobram por minuto (especialmente runners Windows e macOS com multiplicadores de custo mais altos). Jobs que ficam em loop ou compilam do zero a cada execução consomem a cota rapidamente.
2. **Queda de Produtividade dos Desenvolvedores:** Pipelines que demoram mais de 10 minutos quebram o foco do engenheiro, incentivam alternância de contexto (context-switching) e atrasam merges.

As skills de otimização (como a `github-actions-efficiency` do `github/awesome-copilot`) aplicam cinco estratégias principais para cortar o tempo de pipeline pela metade.

---

## 2. Estratégia 1: Caching Inteligente de Dependências

O erro mais comum em pipelines é baixar centenas de megabytes de dependências da internet a cada commit.

### A. Caching Nativo das Setup Actions (Mais Rápido e Limpo):
As actions oficiais já possuem suporte nativo a cache baseado em lockfiles:

```yaml
# Node.js
- uses: actions/setup-node@v4
  with:
    node-version: 20
    cache: 'npm' # ou 'pnpm', 'yarn'

# Python
- uses: actions/setup-python@v5
  with:
    python-version: '3.12'
    cache: 'poetry' # ou 'pip'

# .NET
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'
    cache: true # Ativa cache automático de pacotes NuGet
```

### B. Caching Customizado com Chaves de Fallback (`actions/cache`):
Para linguagens ou artefatos que não possuem setup action integrada:

```yaml
- name: Cache de Dependências
  uses: actions/cache@d4323d4df104b026a6aa633fdb11d772115b6fe6 # v4.2.2
  with:
    path: ~/.m2/repository
    key: ${{ runner.os }}-maven-${{ hashFiles('**/pom.xml') }}
    restore-keys: |
      ${{ runner.os }}-maven-
```

### C. Caching de Camadas Docker (`type=gha`):
Ao compilar imagens Docker, use o backend de cache do próprio GitHub Actions para reaproveitar camadas inalteradas:

```yaml
- name: Build and Push Docker Image
  uses: docker/build-push-action@471d1dc4e07e5cdedd4c2171150001c434f0b7a4 # v6.15.0
  with:
    context: .
    push: true
    tags: myregistry.azurecr.io/pix-core:${{ github.sha }}
    cache-from: type=gha
    cache-to: type=gha,mode=max
```

---

## 3. Estratégia 2: Cancelamento de Execuções Obsoletas (`concurrency`)

Quando um desenvolvedor faz 3 pushes em sequência na mesma branch de Pull Request (por exemplo, corrigindo detalhes rápidos), o comportamento padrão do GitHub é enfileirar e rodar os 3 pipelines até o final.

### Configuração de Cancelamento Automático:
```yaml
# Cancela qualquer execução anterior em andamento para a mesma branch/PR
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```
> [!NOTE]
> Para workflows de **Deploy em Produção**, mantenha `cancel-in-progress: false` para evitar interromper um deploy no meio da operação.

---

## 4. Estratégia 3: Filtragem Inteligente de Caminhos (`paths`)

Evite disparar esteiras pesadas de compilação de código quando apenas documentação ou arquivos de configuração local foram alterados:

```yaml
on:
  pull_request:
    branches: [main]
    paths-ignore:
      - 'docs/**'
      - '**.md'
      - '.gitignore'
      - 'LICENSE'
```

### Monorepos / Múltiplos Serviços (`dorny/paths-filter`):
Em repositórios com múltiplos microsserviços ou frontend + backend, execute apenas os jobs das pastas alteradas no PR:

```yaml
jobs:
  changes:
    runs-on: ubuntu-latest
    outputs:
      backend: ${{ steps.filter.outputs.backend }}
      infra: ${{ steps.filter.outputs.infra }}
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
      - uses: dorny/paths-filter@de90cc6fb38fc0963ad72b210f1f284cd68cea36 # v3.0.2
        id: filter
        with:
          filters: |
            backend:
              - 'src/backend/**'
            infra:
              - 'infra/**'

  test-backend:
    needs: changes
    if: needs.changes.outputs.backend == 'true'
    runs-on: ubuntu-latest
    steps:
      - run: dotnet test
```

---

## 5. Estratégia 4: Poda Consciente de Matrizes (Matrix Pruning)

As matrizes são ideais para bibliotecas open-source que precisam suportar múltiplos sistemas operacionais e versões de runtime. No entanto, executá-las em todo Pull Request interno é desperdício.

### Padrão Recomendado:
- **No Pull Request:** Teste apenas na versão e SO alvo de produção (ex.: .NET 8 no Linux).
- **No Push para main / Release:** Teste na matriz estendida.

```yaml
jobs:
  test:
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: true # Encerra os outros jobs da matriz se um falhar
      matrix:
        os: ${{ github.event_name == 'pull_request' && fromJSON('["ubuntu-latest"]') || fromJSON('["ubuntu-latest", "windows-latest"]') }}
        runtime: ['8.0.x']
```

---

## 6. Estratégia 5: Timeouts Defensivos Obrigatórios

O timeout padrão do GitHub Actions para qualquer job é de **360 minutos (6 horas)**.
Se um processo travar esperando um input interativo no terminal (`stdin`) ou se houver um deadlock de teste, o runner continuará rodando até atingir o limite ou esgotar a cota da conta.

**Regra Absoluta:** Declare `timeout-minutes` em **todos** os jobs:
```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: 15 # Falha rápido se exceder o tempo razoável
```

