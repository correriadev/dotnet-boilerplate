# Azure Cloud Estudos - Sandbox Mínimo de CI/CD

> Branch `main` configurada como ambiente mínimo e autocontido para teste e validação de triggers, runners e fluxos do **GitHub Actions**.

---

## 📌 Contexto das Branches

* **`main` (esta branch):** Projeto mínimo em .NET 8 com endpoint `/health`, testes unitários xUnit com `WebApplicationFactory`, `Dockerfile` alpine multi-stage e esteira de CI/CD rápida e autocontida (`.github/workflows/ci-minimal.yml`).
* **`projeto-completo`:** Contém a solução enterprise completa da **Plataforma Pix** (microsserviços, YARP Gateway, Landing Zone Bicep, testes de arquitetura e a especificação detalhada da esteira de produção em `docs/pipeline/`).

---

## 🚀 Como Executar Localmente

### 1. Compilar e Testar a Solução:
```bash
dotnet restore Pix.HealthCheck.sln
dotnet build Pix.HealthCheck.sln --configuration Release
dotnet test Pix.HealthCheck.sln --configuration Release
```

### 2. Executar a Minimal API:
```bash
dotnet run --project src/Pix.HealthCheck.Api
# Em outro terminal:
curl http://localhost:5000/health
```

### 3. Build e Execução via Docker:
```bash
docker build -t pix-healthcheck:local .
docker run -d -p 8080:8080 --name test-api pix-healthcheck:local
curl http://localhost:8080/health
docker stop test-api && docker rm test-api
```

---

## ⚙️ Esteira de CI/CD (`.github/workflows/ci-minimal.yml`)

A esteira executa automaticamente em cada `push` e `pull_request` direcionado à `main`:
1. **`build-and-test`:** Restaura dependências, compila com tolerância zero a warnings e roda os testes com geração de relatório TRX.
2. **`docker-validation`:** Constrói a imagem Docker Alpine com Buildx, inicializa o contêiner no próprio runner do GitHub e executa uma validação real (`curl -f http://localhost:8080/health`).

