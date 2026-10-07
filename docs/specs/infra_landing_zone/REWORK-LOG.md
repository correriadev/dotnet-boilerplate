# Rework Log — Feature F001 (infra_landing_zone)

**Ciclo:** Rework #1  
**Data:** 2026-10-07  
**Scores Anteriores:** Tech Lead = 0.45 | Adversarial QA = 0.55  
**Meta:** Scores $\ge 0.91$, Zero Vulnerabilidades HIGH/CRITICAL  

---

## 1. Apontamentos Críticos e Vulnerabilidades

### 1.1 [AUTH_BYPASS | HIGH] Isolamento de Rede em `nsgData` e `nsgApps`
- **Diagnóstico:** `nsgData` bloqueava apenas a tag `Internet` com prioridade 4096. Devido à regra padrão do Azure `AllowVnetInBound` (prioridade 65000), qualquer recurso na `snet-gateway` (10.0.1.0/24) podia alcançar diretamente as portas 1433 (SQL), 6379 (Redis), 5671/5672/443 (Service Bus) e 443 (Key Vault) na `snet-data`.
- **Ação:** Inserir regra explícita `Deny-All-Other-Inbound` com prioridade 4000 em `nsgData` e `nsgApps`, bloqueando 100% de qualquer tráfego não explicitamente permitido a partir da `snet-apps`.

### 1.2 [DATA_EXPOSURE | HIGH] Credencial Administrativa Hardcoded em Bicep
- **Diagnóstico:** `data.bicep` definia `@secure() param adminPassword string = 'P@ssw0rdEnterprise2026!'`. Valor padrão hardcoded em parâmetro seguro viola a regra do linter Bicep `secure-parameter-default` e compromete a segurança de produção.
- **Ação:** Remover valor padrão de `adminPassword` em `data.bicep`. Exigir parâmetro seguro em `main.bicep` (`sqlAdminPassword`) sem default estático e configurar passagem de parâmetros via Key Vault e `parameters.dev.json`.

### 1.3 [OTHER | MEDIUM] Incomplete Path Traversal Sanitization e Falsos Positivos
- **Diagnóstico:** `SecurityAndAuditMiddleware` verificava apenas substring `..` literal sem decodificação de URL (permitindo bypass com `%2e%2e` ou `%252e%252e`) e bloqueava arbitrariamente ranges legítimos em query string (ex: `?date=2026-01-01..2026-01-31`).
- **Ação:** Implementar decodificação recursiva de URL (`Uri.UnescapeDataString`), normalização de separadores e regex semântica de directory traversal (`(^|/|\\)\.\.(/|\\|$)`) no path e na query string, eliminando falsos positivos para intervalos numéricos e de datas.

### 1.4 [ARCHITECTURE] Resiliência de Clusters YARP e Timeout Bounded
- **Diagnóstico:** Ausência de limites de timeout de requisição downstream nos clusters YARP, expondo pools de conexão a exaustão durante picos de latência.
- **Ação:** Configurar política `HttpRequest` com `Timeout: 00:00:15` e limites de resiliência em todos os clusters em `appsettings.json` e `appsettings.Development.json`.

### 1.5 [EDGE CASES] Rotas de Raiz de Coleção e Correlação W3C
- **Diagnóstico:** Match path `/api/v1/cashout/{**catch-all}` não casava com a rota de raiz `/api/v1/cashout`. Traceparent aceitava all-zero e não correlacionava com `Activity.Current`.
- **Ação:** Adicionar suporte explícito a rotas de raiz (`/api/v1/cashout` e `/api/v1/pix`), validar formato W3C estrito rejeitando all-zero e integrar com `Activity.Current`.
