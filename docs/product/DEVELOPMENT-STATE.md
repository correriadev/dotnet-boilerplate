| Feature ID | Task ID | Project | Description | Domain | Current Phase | Status |
| --- | --- | --- | --- | --- | --- | --- |
| F001 | T000 | azure-cloud-estudos | Environment parity and cleanup (remove postgres, pin digests, base images, dependabot) | `cicd_pipeline` | - | `COMPLETED` |
| F001 | T001 | azure-cloud-estudos | Executable host Pix.Migrator (DbUp console runner, Dockerfile, expand/contract classification) | `cicd_pipeline` | - | `COMPLETED` |
| F001 | T002 | azure-cloud-estudos | Workflow ci-pr.yml (pyramid execution, layered coverage, N-1 compatibility, parallel 6 Dockerfiles build) | `cicd_pipeline` | - | `COMPLETED` |
| F001 | T003 | azure-cloud-estudos | Workflow package-release.yml (OIDC Entra ID, push ACR, Trivy, SBOM, Cosign, release-manifest.json) | `cicd_pipeline` | - | `COMPLETED` |
| F001 | T004 | azure-cloud-estudos | Workflow deploy-dev.yml (automatic deploy on main merge, Cosign verification, ACA Migrator job, Blue/Green APIs, workers GracePolicy, smoke tests) | `cicd_pipeline` | - | `COMPLETED` |
| F001 | T005 | azure-cloud-estudos | Workflow promote-hml.yml (manual approval gate, Cosign verify, identical 6 digests, Migrator job, integration/contract partner tests, GitHub deployment record) | `cicd_pipeline` | - | `COMPLETED` |
| F001 | T006 | azure-cloud-estudos | Workflow promote-prod.yml (manual approval gate post-HML, Cosign verify, identical 6 digests, Migrator job, Blue/Green 10m bake window, workers GracePolicy, HML validation check via GitHub API) | `cicd_pipeline` | - | `COMPLETED` |
| F001 | T007 | azure-cloud-estudos | Workflow rollback.yml (dynamic schema range check via Pix.Migrator, Cosign verify, instant traffic/workers revert without rebuild, audit log) | `cicd_pipeline` | - | `COMPLETED` |
