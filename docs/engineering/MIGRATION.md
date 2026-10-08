# Protocolo de Migração, Coexistência e Cutover

Este documento formaliza as regras de convivência pacífica entre o sistema Legado e a Nova Aplicação durante a modernização, garantindo integridade de dados e capacidade de reversão.

---

## 1. Princípio do Escritor Único (Single Authoritative Writer)

> [!CAUTION]
> **Regra de Ouro da Concorrência:** Em nenhum momento o Legado e o Novo Sistema podem aceitar escritas concorrentes e arbitrárias para o mesmo agregado de dados.

Durante a vida de uma fatia vertical em produção, a autoridade de escrita segue esta progressão:

```
┌─────────────────────────────────┐
│ FASE 1: LEITURA / SHADOW        │  Legado = Escritor Autorizativo
│ (O Novo projeta ou espelha)     │  Novo = Read-only / Verificação de Paridade
└────────────────┬────────────────┘
                 │ (Após aprovação de cutover)
┌────────────────▼────────────────┐
│ FASE 2: AUTORIDADE NO NOVO      │  Novo = Escritor Autorizativo
│ (Legado bloqueado ou sincroniz.)│  Legado = Read-only ou desativado
└─────────────────────────────────┘
```

---

## 2. Bloqueio Técnico de Escrita no Legado

Não confie apenas em feature flags na camada HTTP. Quando a autoridade migrar para o Novo Sistema:
1. **Revogação de Permissões no Banco:** Remover permissões de `INSERT`/`UPDATE` do usuário de conexão do sistema legado nas tabelas daquele agregado.
2. **Desativação de Jobs Batch:** Pausar ou remover jobs no SQL Server Agent que alterem as entidades migradas.
3. **Fencing Tokens:** Se houver processos concorrentes, utilizar tokens de época ou checks transacionais no banco para barrar escritas de nós antigos com leases expirados.

---

## 3. Roteamento Progressivo com YARP (Strangler Fig)

O **YARP API Gateway** atua na borda do ecossistema roteando o tráfego de entrada:

```json
{
  "ReverseProxy": {
    "Routes": {
      "legacy-fallback-route": {
        "ClusterId": "legacyCluster",
        "Match": { "Path": "{**catch-all}" },
        "Order": 999
      },
      "slice-migrada-route": {
        "ClusterId": "newAppCluster",
        "Match": { "Path": "/api/v1/cobrancas/{**remainder}" },
        "Order": 10
      }
    },
    "Clusters": {
      "legacyCluster": { "Destinations": { "dest1": { "Address": "https://legado.interno/" } } },
      "newAppCluster": { "Destinations": { "dest1": { "Address": "https://novo-app.interno/" } } }
    }
  }
}
```

* **Canary por Coorte:** Para fluxos críticos, o YARP pode usar *header routing* (ex.: `X-Tenant-Id` ou `X-Branch-Id`) para direcionar apenas 1 filial ou tenant específico para o Novo Sistema no primeiro dia.

---

## 4. Runbooks de Rollback: Aplicação vs. Dados

São dois procedimentos completamente distintos:

### Rollback de Aplicação (Instantâneo)
- **Cenário:** O novo serviço apresentou lentidão, estouro de memória ou aumento de erro HTTP.
- **Ação:** Reverter a rota no YARP para o Legado (tempo de execução: < 30 segundos).
- **Pré-requisito:** Os dados gravados pelo novo serviço durante a janela devem ser compatíveis ou absorvidos pelo legado.

### Rollback de Dados (Complexo e Requer Ensaio)
- **Cenário:** A autoridade já havia sido transferida e o novo serviço produziu mutações que o legado desconhece.
- **Ação:** Executar script de compensação / reconciliação reversa pré-desenhado antes do cutover. **Nunca reverter tráfego sem drenar ou reconciliar a fila de eventos gerados.**
