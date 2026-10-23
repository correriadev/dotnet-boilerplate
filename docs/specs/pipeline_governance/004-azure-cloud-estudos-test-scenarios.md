# Test Scenarios: Governança de Entrega, Nomenclatura CAF e Gate de Retrocompatibilidade

## 1. Unit Tests

### 1.1 Aggregates and Value Objects

* **Resource Naming:**
  - [ ] **Should generate correct container and job names when given valid environment**
    * **Given:** Um nome base `app-pix-worker` e ambiente `EnvironmentName("dev")`.
    * **When:** A propriedade `ContainerAppName` é acessada.
    * **Then:** Retorna `app-pix-worker-dev` e o job retorna `job-db-migration-dev`.

  - [ ] **Should throw validation error when given unsupported environment identifier**
    * **Given:** Uma string de ambiente inválida como `staging` ou `qa`.
    * **When:** O `EnvironmentName` é instanciado.
    * **Then:** Lança exceção de validação rejeitando o valor imediatamente sem fallback para DEV.

* **Deployment Phase Transition:**
  - [ ] **Should allow traffic commit only when compatibility verified or bootstrap authorized**
    * **Given:** Uma implantação no estado `CandidateReady`.
    * **When:** O método `CanCommitTraffic` é consultado.
    * **Then:** Retorna falso.

  - [ ] **Should transition to ReconciliationRequired when traffic comutated but persistence fails**
    * **Given:** Tráfego comutado com sucesso no balanceador de ingress.
    * **When:** A gravação do estado canônico no Key Vault sofre falha de I/O ou timeout.
    * **Then:** A entidade transiciona para `DeploymentPhase.ReconciliationRequired`.

### 1.2 Domain Services

* **BackwardCompatibilityEvaluator:**
  - [ ] **Should approve coexistence when legacy revision executes functional write and read on expanded schema**
    * **Given:** Uma execução de teste funcional pelo binário $N-1$ persistindo e consultando dados sobre a tabela expandida com sucesso.
    * **When:** O método `Evaluate` é invocado.
    * **Then:** Retorna `CompatibilityVerdict.Approved`.

  - [ ] **Should reject coexistence when legacy revision fails functional write on schema lacking default value**
    * **Given:** Uma tentativa de inserção pelo binário $N-1$ que resulta em erro SQL devido a uma nova coluna obrigatória sem valor default.
    * **When:** O método `Evaluate` é invocado.
    * **Then:** Retorna `CompatibilityVerdict.Rejected`.

---

## 2. Integration Tests

* **Azure Container Apps Ingress & State Ledger:**
  - [ ] **Should resolve dynamic FQDN enforcing subscription and resource group**
    * **Given:** O recurso `app-pix-worker-dev` provisionado no resource group `rg-lab-observabilidade`.
    * **When:** A CLI executa `az containerapp show --subscription ... --resource-group ... --name ...`.
    * **Then:** Retorna hostname válido e não vazio, adicionando prefixo `https://` sem duplicar scheme.

  - [ ] **Should reject execution when Key Vault deployment state is missing or corrupted**
    * **Given:** Um segredo `DeploymentState` inexistente em um ambiente que já possui revisões ativas no Container Apps.
    * **When:** A esteira tenta resolver o estado prévio.
    * **Then:** Aborta com erro de integridade e não aciona bypass de cold start.

---

## 3. Functional and Acceptance Scenarios (E2E)

### Scenario 1: Deploy com Sucesso em DEV e Gate de Retrocompatibilidade Funcional Aprovado
* **Given:** O ambiente DEV possui a revisão estável `rel-10` atendendo 100% do tráfego.
* **When:** A esteira captura e atesta `rel-10` como baseline estável.
* **And:** O job executa a migração aditiva de schema no banco SQL.
* **And:** A esteira provisiona a candidata `rel-11` com 0% de tráfego público e valida sua prontidão no endpoint dedicado de revisão (`<app>--rel-11`).
* **And:** A esteira executa testes funcionais de leitura e escrita utilizando o binário da revisão estável `rel-10` contra o schema expandido.
* **Then:** A revisão `rel-10` conclui as operações com sucesso sem violação de schema.
* **And:** A esteira comuta tráfego para a revisão `rel-11`, valida convergência no Envoy e grava o `CanonicalDeploymentRecord` no Key Vault com status `COMMITTED`.

### Scenario 2: Falha no Gate de Retrocompatibilidade por Coluna Obrigatória Sem Default
* **Given:** A migração de banco introduz uma coluna `NOT NULL` sem valor padrão em tabela transacional.
* **When:** A esteira de DEV executa o teste funcional de inserção utilizando o binário da revisão estável `rel-10`.
* **Then:** A operação de escrita de `rel-10` falha com violação de constraint no SQL Server.
* **And:** O gate emite veredito `CompatibilityVerdict.Rejected`.
* **And:** A pipeline aborta imediatamente em *Fail-Closed*, mantém a revisão candidata `rel-11` isolada com 0% de tráfego e sinaliza incidente para Fix-Forward.
* **And:** Nenhuma promoção para HOM ou PRD é autorizada.

### Scenario 3: Cold Start em Ambiente Sem Nenhuma Revisão Prévia
* **Given:** O Azure Container Apps retorna lista vazia de revisões (`[]`) para o recurso.
* **When:** A esteira `deploy-dev.yml` avalia a existência de baseline prévio.
* **Then:** Reconhece o cenário de Cold Start legítimo e registra log de auditoria `[INFO] Cold Start detected: No prior revisions found in Container App.`
* **And:** Pula o teste de coexistência N-1 e submete a candidata `rel-01` diretamente ao smoke test funcional.
* **And:** Comuta 100% do tráfego para `rel-01` e grava o registro inicial no Key Vault.

### Scenario 4: Detecção de Falha de Persistência Pós-Comutação de Tráfego
* **Given:** O tráfego foi comutado com sucesso para a nova revisão `rel-12`.
* **When:** A chamada de gravação atômica do segredo `DeploymentState` no Key Vault retorna erro 500 ou timeout.
* **Then:** A esteira não reporta sucesso silencioso.
* **And:** Emite alerta crítico de divergência de estado (`ReconciliationRequired`).
* **And:** Bloqueia execuções subsequentes de deploy e rollback até auditoria manual de SRE.
