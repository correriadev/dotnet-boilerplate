# Walking Skeleton: Guia de Inicialização do Ciclo 0

Este manual estabelece as instruções para gerar, configurar e validar o **Ciclo 0 (Walking Skeleton)** em um novo repositório limpo, conectando a esteira CI/CD aos ambientes de Dev, Homologação e Produção Isolada.

---

## 1. Estrutura Mínima do Repositório Novo

O novo repositório é inicializado a partir do template neutro [`templates/skeleton-host/`](../../templates/skeleton-host/):

```
repo-novo/
├── .github/workflows/
│   └── pipeline.yml                # CI/CD com 3 estágios (Dev -> Hom -> Prod Isolado)
├── infra/
│   └── main.bicep                  # Container Apps + Log Analytics + App Insights/OTLP
├── src/
│   └── App.Api/
│       ├── Program.cs              # Minimal API com /health e W3C Trace Context
│       ├── appsettings.json        # Configuração estruturada sem segredos
│       └── App.Api.csproj          # .NET 8 ou 10 LTS
├── tests/
│   └── App.Api.Tests/
│       ├── HealthCheckTests.cs     # Testes de fumaça e contrato do /health
│       └── App.Api.Tests.csproj
└── App.sln
```

---

## 2. Padrão Canônico do Endpoint `/health`

O endpoint `/health` não deve apenas retornar um texto plano. Ele deve expor o estado do processo e propagar contexto W3C:

```csharp
app.MapGet("/health", (IHostEnvironment env, HttpContext context) =>
{
    var traceParent = context.Request.Headers["traceparent"].FirstOrDefault() ?? Activity.Current?.Id ?? "none";

    return Results.Ok(new
    {
        status = "Healthy",
        timestamp = DateTimeOffset.UtcNow,
        environment = env.EnvironmentName,
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
        traceContext = traceParent
    });
})
.WithName("HealthCheck")
.WithOpenApi();
```

---

## 3. Configuração de Telemetria e Dynatrace

1. **W3C Trace Context:**
   - Todo request recebido deve validar e propagar o header `traceparent` (formato padrão W3C: `00-{traceId}-{spanId}-{flags}`).
2. **OpenTelemetry / Dynatrace OneAgent:**
   - Adicionar os pacotes OpenTelemetry padrão para ASP.NET Core e HttpClient.
   - Configurar o exportador OTLP via variáveis de ambiente (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`).
3. **Dynatrace Quality Gate em Homologação (SRG):**
   - O pipeline executa um smoke test contra o endpoint de Homologação.
   - O Dynatrace Site Reliability Guardian (SRG) valida:
     - Taxa de erro = 0%.
     - Latência P95 < 200ms.
     - Zero spans com erro de rede ou falha de contexto.

---

## 4. Roteiro de Execução do Ciclo 0

```powershell
# 1. Executar o script gerador do chassi (a partir deste boilerplate)
powershell -ExecutionPolicy Bypass -File "./scripts/engine/New-SkeletonProject.ps1" -DestinationPath "C:/caminho/do/novo-repo" -ProjectName "Cliente.Servico"

# 2. Navegar para o novo repositório
cd "C:/caminho/do/novo-repo"

# 3. Testar compilação e testes locais
dotnet test

# 4. Validar execução local
dotnet run --project src/Cliente.Servico.Api
# Em outro terminal: curl http://localhost:5000/health

# 5. Criar commit inicial e disparar a esteira CI/CD
git init
git add .
git commit -m "chore(infra): ciclo 0 - walking skeleton inicializado"
git push origin main
```

**Critério de Aprovação:** Deploy com sucesso em Dev, promoção para Homologação com gate Dynatrace verde e aprovação manual para Produção Isolada.
