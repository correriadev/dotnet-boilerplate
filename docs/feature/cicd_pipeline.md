---
doc_type: feature
domain: cicd_pipeline
stack: [GitHub Actions, Azure Container Apps, ACR, Azure SQL Server, DbUp, Cosign, Syft, Trivy, ORAS, xUnit]
node_id: "feature:cicd-pipeline"
tags: [cicd, github-actions, promotion-gates, oidc, cosign, oras, pix-migrator, blue-green]
edges:
  - relation: implements
    target: "adr:architecture"
    read: must
  - relation: tested_by
    target: "adr:tests"
    read: must
  - relation: depends_on
    target: "feature:qa-pipeline"
    read: must
updated: 2026-10-06
---
```graph
{"node_id":"feature:cicd-pipeline","domain":"cicd_pipeline","implements":["adr:architecture"],"tested_by":["adr:tests"],"entrypoints":[".github/workflows/deploy-dev.yml",".github/workflows/promote-hml.yml",".github/workflows/promote-prod.yml",".github/workflows/rollback.yml"],"registration_files":[".github/dependabot.yml","docker-compose.yml"],"reference_files":["src/Pix.Migrator/Program.cs"],"code_files":[".github/workflows/ci-pr.yml",".github/workflows/package-release.yml","src/Pix.Migrator/Dockerfile","src/Pix.Migrator/Pix.Migrator.csproj"],"test_files":["tests/Unit/CicdPipelineTests.cs"],"knowledge":{"schema_version":1,"entities":[{"id":"capability:release-packaging","type":"capability","label":"Release Packaging","definition":"Package 6 containers with Trivy scan, Syft SBOM, Cosign sign, and ORAS push.","aliases":[]},{"id":"capability:immutable-promotion-gates","type":"capability","label":"Immutable Promotion Gates","definition":"Promote identical 6 container digests across Dev, Hml, and Prod via manual gates.","aliases":[]},{"id":"capability:schema-evolution-control","type":"capability","label":"Schema Evolution Control","definition":"Validate expand/contract migrations and calculate schema ranges via Pix.Migrator.","aliases":[]},{"id":"rule:zero-automatic-promotion","type":"rule","label":"Zero Automatic Promotion","definition":"Automated deployment strictly to Dev; Hml and Prod mandate manual approval gates.","aliases":[]},{"id":"rule:immutable-digest-parity","type":"rule","label":"Immutable Digest Parity","definition":"Exactly the same 6 SHA-256 digests in release-manifest.json deployed across environments.","aliases":[]},{"id":"rule:schema-range-compatibility","type":"rule","label":"Schema Range Compatibility","definition":"Rollback requires active database schema within target manifest range [Min, Max].","aliases":[]},{"id":"contract:release-manifest-spec","type":"contract","label":"Release Manifest Spec","definition":"JSON contract for releaseVersion, commitSha, timestamp, schemaRange, and 6 digests.","aliases":[]},{"id":"contract:pix-migrator-cli","type":"contract","label":"Pix.Migrator CLI","definition":"CLI contract for --get-range, --get-current-schema, and --verify-range.","aliases":[]},{"id":"decision:oci-artifact-storage","type":"decision","label":"OCI Artifact Storage","definition":"Publish release manifests to ACR as OCI artifacts via ORAS.","aliases":[]}],"claims":[{"id":"claim:dev-only-auto-deploy","subject":"capability:immutable-promotion-gates","relation":"constrained_by","object":"rule:zero-automatic-promotion","statement":"Deploy automatic exclusively to Dev; Hml and Prod enforce manual approval gates.","kind":"observation","status":"supported","evidence":[{"kind":"configuration","source":".github/workflows/deploy-dev.yml","locator":"workflow_run trigger","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:immutable-digests-promoted","subject":"capability:immutable-promotion-gates","relation":"constrained_by","object":"rule:immutable-digest-parity","statement":"Promote identical 6 image digests from ACR via ORAS without rebuilding.","kind":"observation","status":"supported","evidence":[{"kind":"configuration","source":".github/workflows/promote-hml.yml","locator":"oras pull and cosign verify","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:schema-range-guard","subject":"capability:schema-evolution-control","relation":"constrained_by","object":"rule:schema-range-compatibility","statement":"Pix.Migrator validates active schema within target release range [Min, Max].","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Migrator/Program.cs","locator":"CalculateSchemaRange and --verify-range","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:manifest-contract-boundary","subject":"capability:release-packaging","relation":"exposes","object":"contract:release-manifest-spec","statement":"Package release produces keyless-signed release-manifest.json with 6 digests.","kind":"observation","status":"supported","evidence":[{"kind":"configuration","source":".github/workflows/package-release.yml","locator":"Assemble release-manifest.json","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:migrator-cli-boundary","subject":"capability:schema-evolution-control","relation":"exposes","object":"contract:pix-migrator-cli","statement":"Pix.Migrator exposes CLI flags for CI/CD pipeline orchestration.","kind":"observation","status":"supported","evidence":[{"kind":"code","source":"src/Pix.Migrator/Program.cs","locator":"Main args handling","snapshot":null}],"derived_from":[],"gap":null},{"id":"claim:oras-storage-decision","subject":"capability:release-packaging","relation":"governed_by","object":"decision:oci-artifact-storage","statement":"Release manifests published to ACR as OCI artifacts via ORAS.","kind":"observation","status":"supported","evidence":[{"kind":"configuration","source":".github/workflows/package-release.yml","locator":"oras push step","snapshot":null}],"derived_from":[],"gap":null}]}}
```

# CI/CD Pipeline & Delivery with Manual Approval Gates

## OVERVIEW
Pipeline corporativo em Azure Container Apps e Azure SQL Server 2022. Quality gate em PR com 158 testes (Domain 90%, App 85%, Infra 80%), empacotamento com Trivy pré-push (bloqueia High/Critical antes do push ao ACR), SBOM Syft, assinatura Cosign keyless, publicação OCI via ORAS, deploy automático exclusivo em Dev, gates manuais em Hml e Prod sem rebuild de imagens, e rollback determinístico com validação de schema.

## FOLDER STRUCTURE
<folder_structure>
```
azure-cloud-estudos/
├── .github/
│   ├── dependabot.yml                  # Atualizações semanais de dependências
│   └── workflows/                      # Workflows de CI, release, deploy e rollback
│       ├── ci-pr.yml                   # Quality gate e validação de compatibilidade N-1
│       ├── package-release.yml         # Build, Trivy local, Syft SBOM, Cosign e ORAS
│       ├── deploy-dev.yml              # Deploy contínuo exclusivo em Dev
│       ├── promote-hml.yml             # Gate manual para Hml com testes de contrato
│       ├── promote-prod.yml            # Gate manual para Prod com bake window e telemetria
│       └── rollback.yml                # Rollback determinístico com verificação de schema
├── src/Pix.Migrator/                   # Console host executável de migrações DbUp
└── tests/Unit/CicdPipelineTests.cs     # Testes unitários dos gates de CI/CD (TS-01 a TS-25)
```
</folder_structure>

## WORKFLOWS
- **ci-pr.yml**: Executa 158 testes, consolida OpenCover multi-projeto e roda testes N-1.
- **package-release.yml**: Autentica OIDC, compila, roda Trivy localmente (bloqueia High/Critical antes do push ao ACR), gera SBOM Syft, envia 6 containers imutáveis ao ACR, assina via Cosign e publica manifesto OCI via ORAS.
- **deploy-dev.yml**: Disparo automático pós-package. Valida Cosign, roda ACA job do `Pix.Migrator` no Dev SQL, atualiza APIs via Blue/Green e smoke tests.
- **promote-hml.yml**: Gate manual com aprovação em `hml`. Baixa manifesto via ORAS, valida Cosign, migra banco Hml, promove os 6 digests sem rebuild e registra status no GitHub.
- **promote-prod.yml**: Gate manual com aprovação em `prod`. Valida execução prévia em Hml via GitHub API (bloqueia salto direto Dev->Prod), roda migrações, aplica canary (10%) com bake window de 10 min e circuit breaker (5xx <= 0.01%, P99 <= 300ms) para reversão automática.
- **rollback.yml**: Reversão sem rebuild. Puxa manifesto via ORAS, valida Cosign, consulta schema ativo via ACA Job no VNet, valida range (`--verify-range`) e restaura tráfego.

## PIX.MIGRATOR SCHEMA GOVERNANCE
- **Expand/Contract**: Proibido parear migrações Expand e Contract para mesma funcionalidade no mesmo release.
- **Timeout DDL**: Timeout de comando de 300s no DbUp (variável `MIGRATION_COMMAND_TIMEOUT_SECONDS`).
- **CLI Commands**: Suporte a `--get-range`, `--get-current-schema` e `--verify-range <current>`.

## PARAMETERS / CONFIGURATIONS
| Parâmetro / Segredo | Escopo | Obrigatório | Descrição |
|---|---|---|---|
| `AZURE_CLIENT_ID` / `HML` / `PROD` | Scoped | Sim | Entra ID Application IDs para autenticação OIDC |
| `ACR_NAME` | Global | Sim | Nome do Azure Container Registry privado |
| `release_version` | Hml / Prod | Sim | Versão monótona do manifesto OCI a promover |
| `target_release_version` | Rollback | Sim | Versão de destino para reversão |

## BEST PRACTICES
REQUIRED: Deploy automático exclusivo em Dev — zero promoção automática para Hml ou Prod.
REQUIRED: Reuso estrito dos 6 digests imutáveis — PROHIBITED: recompilar código na promoção.
REQUIRED: Scan Trivy pré-push — bloquear envio ao ACR se houver vulnerabilidades High ou Critical.
REQUIRED: Assinatura keyless Cosign e proveniência OIDC antes de qualquer implantação.
PROHIBITED: Pareamento de migrações Expand e Contract no mesmo ciclo de release.

## REFERENCES
- [**ARCHITECTURE.md**](../adr/ARCHITECTURE.md): Arquitetura hexagonal e componentes da plataforma Pix.
- [**TESTS.md**](../adr/TESTS.md): Estratégia de testes e quality gate com 158 testes consolidados.
- [**qa_pipeline.md**](./qa_pipeline.md): Pipeline contínua de qualidade e suites de integração.
