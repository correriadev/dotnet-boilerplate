# Canonical Architecture Blueprint & Modernization Methodology

Este documento estabelece o **contrato canônico de engenharia** para a modernização do sistema legado utilizando uma abordagem orientada a agentes de IA.

> [!IMPORTANT]
> **Princípio da Desidratação:** O código e as entidades presentes no repositório de estudos (ex.: `Pix`, `SPI Liquidação`, `PedidoPix`) são **exemplos ilustrativos de laboratório**. Eles **não devem ser transportados** para o novo repositório de produção do cliente. O novo repositório herda apenas os padrões técnicos canônicos, as práticas de engenharia e os componentes de infraestrutura aqui formalizados.

---

## 1. Visão Geral da Metodologia Fractal

A modernização adota o princípio de Alistair Cockburn e Kent Beck (**"Make it work, make it right, make it fast"**) estruturado de forma auto-similar (**fractal**):

```
                     CICLO 0: O WALKING SKELETON
      (Chassi Mínimo + /health + Dynatrace W3C + CI/CD Prod Isolado)
                                 │
                                 ▼
    ┌──────────────────────────────────────────────────────────────┐
    │              CICLO FRACTAL (POR FATIA VERTICAL)              │
    │                                                              │
    │  [TEMPO 1: ORÁCULO]       Fixar testes de contrato do legado │
    │         │                                                    │
    │  [TEMPO 2: A TRIPA]       Fazer a regra passar em 1 camada   │
    │         │                                                    │
    │  [TEMPO 3: CRISTALIZAR]   Refatorar para Clean/Hexagonal     │
    │         │                                                    │
    │  [TEMPO 4: ENRIQUECER]    Plugar Redis, Outbox, Service Bus  │
    └──────────────────────────────────────────────────────────────┘
                                 │
              (Repete para cada funcionalidade até o corte)
```

---

## 2. Ciclo 0: O Walking Skeleton (Chassi Base)

Antes de qualquer linha de negócio ser migrada, o novo repositório deve nascer como um sistema vivo que compila, é observado e é implantado automaticamente:

1. **Host Executável Mínimo:** Minimal API em .NET (.NET 8 ou .NET 10 LTS) sem acoplamento a domínios legados.
2. **Endpoint de Liveness/Readiness:** `GET /health` respondendo `200 OK` com metadados estruturados.
3. **Observabilidade Contínua:**
   - W3C Trace Context ponta a ponta (`traceparent` com validação de 55 caracteres).
   - Injeção de OpenTelemetry e Dynatrace OneAgent.
   - Sanitização de logs sem PII.
4. **Esteira CI/CD de 3 Estágios:**
   - `Dev`: Deploy contínuo automatizado.
   - `Homologação`: Deploy com quality gate de observabilidade (Dynatrace SRG / Davis AI).
   - `Produção Isolada`: Deploy aprovado manualmente em ambiente contido na Azure Landing Zone.

---

## 3. O Ciclo Fractal em 4 Tempos (Para Cada Fatia de Negócio)

### Tempo 1: Fixação do Oráculo (Contratos e Caracterização)
* **Objetivo:** Capturar a realidade empírica do legado sem julgamento moral sobre a beleza do código.
* **Mecânica:**
  1. Extrair os contratos de entrada e saída (JSON, XML, RPC).
  2. Inspecionar o banco do legado: identificar tabelas afetadas, triggers disparadas e efeitos colaterais.
  3. Criar fixtures com snapshots de estado sanitizados.
  4. Escrever testes de contrato/integração no novo repositório.
* **Critério de Saída:** Os testes de contrato estão comitados no novo repositório e **rodam em estado VERMELHO (falhando)** por ausência de implementação.

### Tempo 2: A "Tripa de Código" (Outside-In em Camada Única)
* **Objetivo:** Fazer a regra de negócio e a persistência passarem rapidamente, focando 100% do orçamento cognitivo da IA na lógica.
* **Mecânica:**
  1. Construir o endpoint diretamente no arquivo da API (ou arquivo contíguo da slice).
  2. Implementar toda a lógica em camada única: validações inline, cálculo de negócio e queries diretas ao banco (via Dapper/SQL bruto).
* **Restrições de Segurança Inegociáveis (Não Postergar):**
  - Todo SQL deve ser rigorosamente parametrizado (zero tolerância a SQL Injection).
  - Transações com banco devem ser gerenciadas via `TransactionScope` ou `DbTransaction` explícito.
  - Autenticação e autorização mínimas devem ser aplicadas.
* **Exceção Arquitetural:** A "tripa" recebe uma anotação de exceção arquitetural temporária (`[TemporarySliceException]`). **É terminantemente proibido promover uma fatia em estado de tripa para ambientes de Homologação ou Produção.**
* **Critério de Saída:** Todos os testes de contrato do Tempo 1 viram **VERDES**.

### Tempo 3: Cristalização Arquitetural (Hexagonal / Clean)
* **Objetivo:** Desmembrar a tripa em uma arquitetura sustentável sem quebrar o comportamento de negócio já aprovado.
* **Mecânica:**
  1. **Domain:** Extrair entidades, Value Objects e invariantes de validação puras.
  2. **Application:** Criar Commands/Queries, Handlers e declarar as Portas (interfaces) de saída.
  3. **Infrastructure:** Criar Adapters concretos para persistência (Dapper/DbUp) e serviços externos.
  4. **API:** O endpoint é reduzido a um despachante limpo.
* **Travas Determinísticas:**
  - Os testes de contrato do Tempo 1 são reexecutados sem alteração de asserções. Devem permanecer **100% VERDES**.
  - O **NetArchTest** valida as regras de dependência das camadas.
  - A anotação de exceção de tripa é removida.
* **Critério de Saída:** Código refatorado, testes de contrato verdes e testes de arquitetura 100% aprovados.

### Tempo 4: Enriquecimento de Escala e Resiliência (Sob Demanda)
* **Objetivo:** Adicionar componentes avançados apenas quando o requisito não-funcional da fatia exigir:
  - **Idempotência & Cache:** Decorators com Redis (fail-closed resiliente com fallback local em memória).
  - **Efeitos Colaterais Assíncronos:** Gravação na tabela de `Outbox` na mesma transação da regra de negócio e despacho via `OutboxRelay` no Azure Service Bus com FIFO Sessions.
  - **Resiliência:** Políticas de retry e circuit breaker via Polly v8 com timeouts estritos.
* **Critério de Saída:** Testes de integração de infraestrutura e caos aprovados; promoção liberada para Homologação e Produção.

---

## 4. Regras Canônicas de Dependência e Código

```
    ┌──────────────────────────────────────────────┐
    │                     API                      │ (Controllers / Minimal APIs)
    └──────────────────────┬───────────────────────┘
                           │ depende de
    ┌──────────────────────▼───────────────────────┐
    │                 APPLICATION                  │ (Commands, Handlers, Ports)
    └──────────────┬───────────────────────────────┘
                   │ depende de
    ┌──────────────▼───────────────────────────────┐
    │                    DOMAIN                    │ (Entidades, VOs - Zero Deps)
    └──────────────────────────────────────────────┘
                           ▲
                           │ implementa Ports
    ┌──────────────────────┴───────────────────────┐
    │                INFRASTRUCTURE                │ (Adapters, Dapper, DbUp, Redis)
    └──────────────────────────────────────────────┘
```

1. **Domain é Isolado:** `Domain` não faz referência a nenhum outro projeto da solution e não importa namespaces de infraestrutura (Entity Framework, Dapper, SQLClient, Redis).
2. **Application depende apenas de Domain:** `Application` define interfaces de portas (`IPort`, `IRepository`), mas nunca instancia adaptadores diretamente.
3. **Infrastructure implementa Application:** Adaptadores residem em `Infrastructure` e são registrados na injeção de dependência via extensão (`AddInfrastructureServices()`).
4. **Persistência Unificada:** Dapper + DbUp é o padrão primário para transações de alta performance e evolução de schema determinística. Se EF Core for usado para fluxos específicos, deve compartilhar a mesma conexão e transação do Dapper.
