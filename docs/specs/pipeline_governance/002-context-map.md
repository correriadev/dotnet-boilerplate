# Strategic Design — Context Map: Governança de Entrega e Retrocompatibilidade Multiambiente

## 1. Bounded Context Identification

| Bounded Context | Responsibility | Boundary (excluded) | Team Ownership | Key Entities |
|---|---|---|---|---|
| **Release Packaging & Attestation** | Orquestrar compilação única, geração de SBOM, auditoria de segurança (Trivy) e assinatura digital do manifesto de release. | Não gerencia provisionamento de infraestrutura nem execução de migrações de dados. | SecOps & Plataforma | `ReleaseManifest`, `ArtifactDigest`, `DigitalSignature` |
| **Data Migration & Schema Evolution** | Executar migrações relacionais aditivas sob lock distribuído e verificar a matriz de compatibilidade de esquemas. | Não comuta tráfego de rede nem gerencia o ciclo de vida dos contêineres de aplicação. | DBA & Engenharia de Dados | `MigrationExecution`, `SchemaVersionHistory`, `DistributedLock` |
| **Traffic Routing & Canary Gate** | Controlar pesos de roteamento no balanceador, coordenar períodos de bake/canary e atestar a convergência de tráfego. | Não valida regras internas do domínio PIX nem executa builds. | SRE & Plataforma | `TrafficWeightAllocation`, `CanaryBakeSession`, `RevisionRoute` |
| **Backward Compatibility & Rollback Verification** | Submeter a revisão estável anterior ($N-1$) a testes de leitura e escrita sobre o schema novo migrado ($N$) antes da comutação de tráfego. | Não corrige dados corrompidos nem executa rollbacks de schema destrutivos. | QA & Engenharia de Software | `CompatibilityTestRun`, `CoexistenceVerdict`, `RollbackFallbackTrigger` |
| **Deployment State Ledger** | Manter o registro distribuído de auditoria canônica do estado por ambiente no Azure Key Vault. | Não armazena dados transacionais de negócio nem substitui banco relacional. | Governança & SRE | `CanonicalDeploymentRecord`, `AuditLedger` |

---

## 2. Context Map

```
[Release Packaging & Attestation] 
       │ (Published Language / Open Host Service)
       ▼
[Data Migration & Schema Evolution] ──(Customer-Supplier)──► [Backward Compatibility & Rollback Verification]
       │                                                                  │
       │                                                                  ▼
       └─────────────────────────► [Traffic Routing & Canary Gate] ◄──────┘
                                                  │
                                                  │ (Published Language / Document Store)
                                                  ▼
                                     [Deployment State Ledger]
```

### Detalhamento das Relações

* **[Release Packaging & Attestation] → [Data Migration & Schema Evolution]**
  * **Pattern:** Published Language (PL) / Open Host Service (OHS)
  * **Direction:** Upstream (Packaging) → Downstream (Data Migration)
  * **Justification:** O manifesto de release (`release-manifest.json`) atua como contrato assinado e imutável consumido por todos os estágios posteriores.

* **[Data Migration & Schema Evolution] → [Backward Compatibility & Rollback Verification]**
  * **Pattern:** Customer-Supplier
  * **Direction:** Upstream (Migration) → Downstream (Compatibility Gate)
  * **Justification:** A migração do schema $N$ expande a base de dados, fornecendo o substrato sobre o qual o gate de compatibilidade submete a revisão $N-1$ a testes de consulta e persistência.

* **[Backward Compatibility & Rollback Verification] → [Traffic Routing & Canary Gate]**
  * **Pattern:** Customer-Supplier (com Quality Gate imperativo)
  * **Direction:** Upstream (Compatibility Gate) → Downstream (Traffic Routing)
  * **Justification:** O roteador de tráfego só recebe autorização para comutar tráfego para a revisão candidata $N$ após o veredito positivo emitido pelo gate de compatibilidade.

* **[Traffic Routing & Canary Gate] → [Deployment State Ledger]**
  * **Pattern:** Published Language
  * **Direction:** Upstream (Traffic Routing) → Downstream (State Ledger)
  * **Justification:** O roteador persiste o registro canônico atômico em JSON estruturado no Key Vault após confirmar a convergência da comutação.

---

## 3. Core Domain Highlight

```
Context   : Backward Compatibility & Rollback Verification + Traffic Routing & Canary Gate
Reason    : Em ecossistemas transacionais de alta criticidade (como o PIX), a capacidade de comprovar que uma atualização de banco não quebra o sistema estável e a habilidade de direcionar tráfego progressivamente são os principais garantidores de continuidade operacional.
Investment: Modelagem tática rigorosa da máquina de estados de tráfego, automação determinística de testes funcionais de leitura/escrita em DEV e sondagem contínua de convergência no Envoy Ingress.
```

---

## 4. Architectural Decisions (ADRs)

### ADR-01: Padronização Explícita de Nomenclatura CAF com Validação de Identidade
* **Context:** A assimetria entre nomes de recursos (`app-pix-worker` vs `app-pix-worker-hml`) gera confusão operacional em incidentes. Nomes sozinhos, porém, não garantem segurança.
* **Decision:** Padronizar nomes com sufixos de ambiente (`app-pix-worker-dev`, `app-pix-worker-hml`, `app-pix-worker-prod` e `job-db-migration-*`) acompanhados de validação obrigatória da Subscription e Managed Identity/OIDC do ambiente antes de qualquer comando de mutação.
* **Consequences:** Garante clareza em logs/portais e mitiga erros de operação manual; exige planejamento de migração e criação de recursos nos ambientes onde o nome ainda é genérico.
* **Status:** Aprovado.

### ADR-02: Gate de Retrocompatibilidade Mandatório com Teste Funcional de Coexistência em DEV
* **Context:** Conectar com sucesso ao banco (`SELECT 1`) não comprova que a versão $N-1$ continua compatível com tabelas modificadas pela versão $N$.
* **Decision:** Antes de promover a revisão $N$, a esteira de DEV:
  1. Captura a revisão estável $N-1$ e seu status antes de alterar qualquer recurso.
  2. Executa a migração aditiva de schema.
  3. Testa a prontidão da nova revisão $N$ em seu endpoint dedicado (`<app>--rel-X`).
  4. Executa testes funcionais de leitura e escrita utilizando o binário $N-1$ contra o schema expandido.
  5. Se o teste de $N-1$ falhar, aborta a esteira em *Fail-Closed*, sinaliza incidente e mantém a revisão $N$ com 0% de tráfego, exigindo correção via Fix-Forward.
  6. Se o teste passar, comuta tráfego para a revisão $N$ e valida convergência.
* **Consequences:** Elimina falsos positivos de compatibilidade em banco e blinda homologação e produção contra quebras silenciosas de schema.
* **Status:** Aprovado.

### ADR-03: Proibição de Scripts DOWN Destrutivos Automáticos e Registro Canônico Atômico
* **Context:** A execução automática de scripts `DOWN` em bancos relacionais acarreta perda irreversível de dados. Gravar estado de implantação em segredos dispersos gera incoerência em caso de falha intermediária.
* **Decision:** Adotar estritamente a disciplina de *Expand and Contract* (migrações forward aditivas). Persistir o estado da implantação como um registro JSON canônico atômico no Key Vault (`CanonicalDeploymentRecord`), bloqueando novas esteiras concorrentes e exigindo reconciliação em caso de queda de runner.
* **Consequences:** Preservação estrita dos dados e rastreabilidade total de auditoria para SRE e Tech Leads.
* **Status:** Aprovado.
