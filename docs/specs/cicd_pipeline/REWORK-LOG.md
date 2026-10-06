# Rework Log — Feature F001 (`cicd_pipeline`)

## Rework Cycle #1

### Open Points from Tech Lead (`the-grumpy-tech-lead`):
1. **Artifact Distribution Across Workflows:** `actions/download-artifact@v4` without `run-id` fails in `workflow_dispatch` runs (`promote-hml.yml`, `promote-prod.yml`, `rollback.yml`). Store and pull `release-manifest.json` directly from ACR as an OCI artifact (via ORAS / Azure CLI) or download from the originating release run via GitHub CLI (`gh run download`).
2. **Dynamic Database Schema Introspection in Rollback:** `rollback.yml` hardcoded `$currentSchema = 8`. Must query the active database in Azure SQL (via `sqlcmd` or `dotnet run --project src/Pix.Migrator -- --verify-range`) to validate schema compatibility dynamically.
3. **Strict HML Validation Prerequisite in Prod:** `promote-prod.yml` must cryptographically or statefully verify that the target release version was successfully deployed and verified in `hml` (e.g. inspecting GitHub Deployments API for `environment: hml` with `state: success`).
4. **Real Coverage and N-1 Test Execution in PR:** `ci-pr.yml` must parse the generated OpenCover XML to enforce per-layer coverage thresholds (Domain 90%, App 85%, Infra 80%, Global 85%) and invoke the actual test runner for N-1 backward compatibility when migrations are modified.

### Vulnerabilities & Edge Cases from Adversarial QA (`adversarial-qa`):
1. **[HIGH | AUTH_BYPASS]:** `promote-prod.yml` allows bypassing HML because the verification step is only a string comparison against inputs without verifying prior successful HML execution.
2. **[HIGH | DATA_INTEGRITY]:** `rollback.yml` risk of deploying incompatible code against evolved database schema due to hardcoded current schema value.
3. **[MEDIUM | SUPPLY_CHAIN]:** `package-release.yml` used mutable `aquasec/trivy:latest` mounted to Docker socket. Pin the Trivy action/image by digest or use official GitHub Action.
4. **[TIER 3 | -0.05]:** `package-release.yml` hardcoded `schemaRange: min=1, max=8`. Compute schema range dynamically by executing `Pix.Migrator` output.
