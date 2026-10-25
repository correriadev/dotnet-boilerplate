# Product Backlog Building (PBB) — Testes Funcionais e de Contrato com Bruno

## 1. Product

* **Nome do Produto:** Automação de Testes de API Funcionais e Contrato com Bruno (Bruno API Automated Testing).
* **Objetivo:** Estabelecer testes de API automatizados, declarativos e versionados diretamente no Git com Bruno (`@usebruno/cli`), substituindo ferramentas proprietárias e validando contratos HTTP, regras de status e saúde das dependências da API Pix.
* **Fronteiras do Produto:** Abrange a coleção de testes declarativos `.bru`, ambientes (`local`, `dev`, `hml`, `prod`), asserções estruturais do payload JSON do `/health`, relatórios multiplataforma (JUnit, HTML, JSON, Step Summary) e esteira GitHub Actions. Não substitui os testes unitários do .NET nem os testes de carga com k6.
* **Intenção do Produto:** Fornecer feedback funcional imediato pós-deploy em desenvolvimento e homologação ("Shift-Left Black-Box Verification"), garantindo que nenhuma revisão com falha de integração ou quebra de contrato avance na esteira.

---

## 2. Problems

* **P01 — Dependência de ferramentas de API acopladas a nuvens proprietárias:** Dificuldade de sincronização e governança de coleções Postman que exigem contas pagas, sincronismo em nuvem externa ou geram conflitos no Git.
* **P02 — Falta de validação funcional estruturada pós-deploy:** Verificações pós-deploy dependentes apenas de scripts ad-hoc em PowerShell sem relatórios padronizados de asserção (JUnit/HTML).
* **P03 — Risco de regressão silenciosa em endpoints de saúde:** Mudanças no formato do payload retornado pelo `/health` que quebram consumidores e orquestradores de monitoramento sem detecção imediata.
* **P04 — Dificuldade de reprodução de bugs pelo desenvolvedor:** Desenvolvedores precisam recriar manualmente requisições HTTP e headers para reproduzir falhas ocorridas na esteira de CI/CD.

---

## 3. Expectations

* **E01 — Coleção Git-First em formato de texto simples (.bru):** Toda a definição de requisições, variáveis e scripts de teste armazenada em arquivos de texto plano legíveis no repositório Git.
* **E02 — Verificação funcional bloqueante (Quality Gate):** Interrupção automática da esteira (Fail-Closed) em caso de falha em qualquer asserção funcional.
* **E03 — Relatórios ricos integrados ao GitHub Actions:** Geração de relatórios JUnit (para testes do GitHub), HTML (para auditoria humana) e Step Summary.
* **E04 — Modularidade de ambientes:** Suporte nativo a múltiplos ambientes (`local`, `dev`, `hml`, `prod`) sem duplicação de requisições.
* **E05 — Complementaridade com testes de carga (k6):** Bruno valida corretude funcional e integridade de contrato; k6 valida latência sob concorrência e vazão.

---

## 4. Features & Backlog Items (PBIs)

### [PBI-BRU-01] Estrutura da Coleção Bruno e Cenário de Smoke para `/health`
* **Descrição:** Como Engenheiro de Software, quero uma coleção Bruno versionada no Git com teste de smoke para `GET /health`, para validar status HTTP 200, Content-Type JSON e integridade do payload de dependências.
* **Critérios de Aceitação (Gherkin):**
  - **Cenário 1: Validação de sucesso do `/health`**
    * *Dado* que a API está em execução no ambiente selecionado
    * *Quando* o Bruno CLI executar o cenário `smoke/health.bru`
    * *Então* deve validar status 200, cabeçalho `Content-Type: application/json` e tempo de resposta inferior ao limite do ambiente
    * *E* deve validar que `body.status` é igual a `"Healthy"` e que o objeto `entries` está presente.
* **Tarefas Técnicas:**
  - Criar `tests/bruno/bruno.json` e `tests/bruno/package.json`.
  - Criar `tests/bruno/environments/` (`local.bru`, `dev.bru`, `hml.bru`, `prod.bru`).
  - Criar `tests/bruno/smoke/health.bru` com asserts e scripts de teste.

---

### [PBI-BRU-02] Esteira Automatizada do Bruno no GitHub Actions (`bruno-api-test.yml`)
* **Descrição:** Como Engenheiro DevOps, quero uma esteira dedicada que execute o Bruno CLI e gere relatórios JUnit, HTML e Step Summary, para operar tanto sob demanda quanto como gate reutilizável.
* **Critérios de Aceitação (Gherkin):**
  - **Cenário 1: Disparo manual ou via esteira principal**
    * *Dado* a chamada da esteira passando o ambiente e a pasta de teste
    * *Quando* o job executar `bru run`
    * *Então* deve exportar `reports/junit.xml`, `reports/report.html` e `reports/report.json`
    * *E* deve publicar o resumo no `$GITHUB_STEP_SUMMARY` e falhar o job se houver teste quebrado.
* **Tarefas Técnicas:**
  - Criar `.github/workflows/bruno-api-test.yml`.
  - Adicionar extração de sumário de testes do JUnit para o Step Summary.

---

### [PBI-BRU-03] Orquestração Integrada dos Gates de QA (Bruno + k6) na Esteira de Entrega
* **Descrição:** Como Tech Lead, quero que a esteira principal de deploy (`package-release-deploy.yml`) execute o Bruno (funcional) e o k6 (carga) de forma sequencial e coordenada nos ambientes DEV e HML.
* **Critérios de Aceitação (Gherkin):**
  - **Cenário 1: Gate de qualidade pós-deploy em DEV**
    * *Dado* que o deploy em DEV foi concluído
    * *Quando* os testes de smoke do Bruno forem executados com sucesso
    * *Então* deve disparar o smoke test do k6 antes de aprovar o avanço para homologação.
  - **Cenário 2: Gate de qualidade em HML antes da promoção para PRD**
    * *Dado* que a revisão em HML está ativa
    * *Quando* o Bruno validar todas as jornadas e o k6 comprovar os SLOs de latência
    * *Então* a versão deve ser considerada apta para Canary em produção.
* **Tarefas Técnicas:**
  - Atualizar `.github/workflows/package-release-deploy.yml` integrando chamadas ou jobs dedicados de QA.

