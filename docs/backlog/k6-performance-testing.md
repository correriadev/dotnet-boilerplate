# Product Backlog Building (PBB) — Testes de Performance com k6 e Governança de SLOs

## 1. Product

* **Nome do Produto:** Módulo de Testes de Performance com k6 e Portas de Qualidade de SLOs (k6 Performance Testing & SLO Quality Gates).
* **Objetivo:** Estabelecer testes de desempenho automatizados, reproduzíveis e orientados a SLOs para os serviços da plataforma Pix, validando latência, vazão e taxa de erro a partir do endpoint fundamental `/health` antes de expandir para jornadas financeiras complexas.
* **Fronteiras do Produto:** Abrange a infraestrutura de scripts k6 modularizados, validações de contrato HTTP/JSON, cenários de Smoke e Load testing, esteira GitHub Actions com relatórios em Step Summary e integração como porta de qualidade (Quality Gate) pós-deploy. Não substitui os testes unitários da aplicação nem abrange testes de penetração de segurança profunda.
* **Intenção do Produto:** Garantir que nenhuma versão seja promovida ou receba tráfego no Azure Container Apps sem demonstrar adesão comprovada aos orçamentos de latência (P95 < 200ms, P99 < 500ms) e disponibilidade de 99.9% sob carga representativa.

---

## 2. Problems

* **P01 — Ausência de baseline quantitativo de latência e concorrência:** Deploys no Azure Container Apps ocorrem sem validação padronizada de tempos de resposta sob múltiplos usuários virtuais simultâneos.
* **P02 — Scripts ad-hoc com amostragem insuficiente ou distorcida:** Utilização de loops simples em PowerShell para testar APIs não simula conexões simultâneas realistas nem calcula percentis estatísticos precisos (P95/P99).
* **P03 — Risco de cold start e saturação de réplicas silenciosos:** Novas revisões de contêiner podem apresentar inicialização lenta de conexões com banco de dados, Redis ou Key Vault sem detecção prévia antes da chegada do tráfego real.
* **P04 — Falta de visibilidade executiva e técnica dos testes de carga:** Desenvolvedores e mantenedores da esteira precisam inspecionar logs brutos de console para entender o motivo de reprovações de desempenho.
* **P05 — Acoplamento e complexidade desnecessária para execução local:** Dificuldade de desenvolvedores executarem os mesmos testes de estresse em suas máquinas locais contra instâncias locais ou ambientes de desenvolvimento.

---

## 3. Expectations

* **E01 — Verificação imediata e determinística (Smoke Test):** Execução de carga ultraleve (1 a 2 VUs por 30s) logo após o deploy para validar integridade operacional, conectividade com dependências e contrato do `/health`.
* **E02 — Portas de qualidade bloqueantes com thresholds de SLO:** Reprovação imediata da esteira de CI/CD (Fail-Closed com exit code != 0) caso percentis de latência ou taxa de falhas excedam limites contratuais.
* **E03 — Transparência no GitHub Actions (Markdown Step Summary):** Geração automática de tabelas com percentis (Média, Mediana, P95, P99), taxas de sucesso e status de cada threshold diretamente no sumário da execução.
* **E04 — Modularidade e parametrização multiambiente:** Capacidade de executar os mesmos scripts em `local`, `dev`, `hml` ou `prod` via variáveis de ambiente (`BASE_URL`, `ENVIRONMENT`, `TEST_TYPE`).
* **E05 — Base extensível para jornadas transacionais:** Arquitetura de código em JavaScript modular pronta para receber endpoints de pagamentos, transferências e consultas Pix.

---

## 4. Personas

### Persona 1: Engenheiro de Software / Desenvolvedor .NET
* **Objetivo Primário:** Validar que alterações no código, queries de banco ou injeções de dependência não degradaram a latência da API.
* **Interações Relevantes:** Execução local do k6 antes do PR e consulta ao Step Summary gerado na esteira do GitHub Actions.
* **Problemas e Expectativas Vinculados:** P01, P05 / E01, E03, E04.

### Persona 2: Engenheiro de Plataforma / SRE / DevOps
* **Objetivo Primário:** Assegurar que os contêineres no Azure Container Apps sustentem a volumetria estipulada de SLO e que o autoscaling responda adequadamente.
* **Interações Relevantes:** Manutenção da esteira reutilizável de testes de carga, calibração de thresholds de SLO e integração em gates de Homologação.
* **Problemas e Expectativas Vinculados:** P02, P03 / E02, E03.

---

## 5. Features & Backlog Items (PBIs)

### [PBI-K6-01] Estrutura Modular de Scripts k6 para o Endpoint `/health`
* **Descrição:** Como Engenheiro de Software, quero scripts JavaScript modulares para testar o endpoint `/health` com suporte a perfis `smoke` e `load`, para que eu possa avaliar a saúde da aplicação em diferentes níveis de concorrência.
* **Critérios de Aceitação (Gherkin):**
  - **Cenário 1: Smoke test do `/health` com sucesso**
    * *Dado* que a API está ativa no ambiente de destino
    * *Quando* o k6 executar com `TEST_TYPE=smoke` (2 VUs por 30 segundos)
    * *Então* deve validar status HTTP 200, header `Content-Type: application/json` e payload contendo `status: "Healthy"`
    * *E* a taxa de checks deve ser de 100% e taxa de falhas HTTP < 1%.
  - **Cenário 2: Load test do `/health` com verificação de SLOs**
    * *Dado* que a API está ativa no ambiente de homologação
    * *Quando* o k6 executar com `TEST_TYPE=load` (ramp-up até 20 VUs)
    * *Então* deve validar que a latência P95 é menor que 200ms e P99 menor que 500ms
    * *E* deve falhar com código de saída diferente de zero se qualquer threshold for violado.
* **Tarefas Técnicas:**
  - Criar `tests/k6/health.js` com requisição e asserções de contrato.
  - Criar `tests/k6/lib/config.js` com validações de ambiente, URL e configuração de cenários k6.

---

### [PBI-K6-02] Geração e Exportação de Relatórios de Performance (Summary Reporter)
* **Descrição:** Como SRE, quero que a execução do k6 exporte relatórios consolidados em Markdown e JSON sem requerer dependências externas, para disponibilizar métricas claras aos times e arquivamento auditável.
* **Critérios de Aceitação (Gherkin):**
  - **Cenário 1: Exportação de sumário em Markdown e JSON**
    * *Dado* o término da execução de uma suíte k6
    * *Quando* o lifecycle hook `handleSummary()` for invocado
    * *Então* deve gerar `results/summary.md` contendo tabela de métricas e status dos thresholds
    * *E* deve gerar `results/summary.json` contendo a árvore completa de telemetria.
* **Tarefas Técnicas:**
  - Criar `tests/k6/lib/summary.js` formatando métricas em Markdown e JSON.
  - Atualizar `.gitignore` para ignorar o diretório `results/`.

---

### [PBI-K6-03] Esteira Automatizada de Performance no GitHub Actions (`k6-performance.yml`)
* **Descrição:** Como Engenheiro DevOps, quero uma esteira dedicada que suporte execução manual sob demanda (`workflow_dispatch`) e invocação orquestrada por outros fluxos (`workflow_call`), para desacoplar a governança de testes de carga.
* **Critérios de Aceitação (Gherkin):**
  - **Cenário 1: Execução sob demanda via workflow_dispatch**
    * *Dado* que o operador seleciona o ambiente (`dev`, `hml`, `prod`) e o tipo de teste (`smoke`, `load`)
    * *Quando* a esteira for disparada manualmente
    * *Então* deve validar os parâmetros de entrada, executar o k6 via ação oficial `grafana/setup-k6-action`
    * *E* deve publicar o sumário no `$GITHUB_STEP_SUMMARY` e persistir os artefatos por 14 dias.
  - **Cenário 2: Invocação reutilizável via workflow_call**
    * *Dado* que uma esteira de entrega contínua concluiu o deploy no Azure Container Apps
    * *Quando* invocar o workflow `k6-performance.yml` passando o ambiente e o tipo de teste
    * *Então* deve retornar sucesso se todos os thresholds passarem, ou falhar o pipeline caso haja violação de SLO.
* **Tarefas Técnicas:**
  - Criar `.github/workflows/k6-performance.yml` com validação de inputs e job de execução k6.
  - Adicionar Step Summary e upload de artefatos.

---

### [PBI-K6-04] Extensão para Cenários de Estresse e Testes Noturnos (Nightly / Soak)
* **Descrição:** Como Arquiteto de Software, quero cenários de carga contínua (Soak) e rajada (Spike) executando fora do caminho crítico de entrega, para identificar vazamentos de memória e comportamento do autoscaling.
* **Critérios de Aceitação (Gherkin):**
  - **Cenário 1: Execução programada noturna**
    * *Dado* o cronograma de testes noturnos
    * *Quando* o cenário de Soak test for acionado
    * *Então* deve monitorar estabilidade por período prolongado (ex: 30 minutos a 1 hora) sem degradação linear de memória ou latência.
* **Tarefas Técnicas:**
  - Adicionar perfil `stress` ao `tests/k6/lib/config.js`.
  - Documentar estratégia de testes noturnos no repositório.
