# Contratos Formais e Papéis de Agentes

Este documento define os **6 papéis formais de agentes**, seus escopos estritos de arquivo, suas entradas, saídas e os limites de atuação para evitar alucinações e perda de foco cognitivo.

---

## 1. Matriz de Responsabilidades e Limites de Arquivo

| Papel | Missão Principal | Escopo de Arquivos Permitido | Proibições Estritas |
| :--- | :--- | :--- | :--- |
| **1. Orquestrador de Fatias** | Gerencia a máquina de estados e valida gates do `LIFECYCLE.md`. | `docs/slices/`, `handoff.json`, `slice.json` | **PROIBIDO** escrever ou editar arquivos de código fonte (.cs, .sql, .bicep). |
| **2. Especialista em Legado e Oráculo** | Minera regras do legado e constrói o teste de contrato/caracterização. | `tests/Contracts/`, `fixtures/` | **PROIBIDO** tocar na implementação do novo repo ou flexibilizar asserções para o implementador. |
| **3. Implementador da Fatia (Tripa)** | Escreve código contíguo em 1 camada até o oráculo ficar 100% verde. | 1 arquivo de slice em `src/.../Endpoints/` | **PROIBIDO** criar novas camadas ou projetos antecipadamente; **PROIBIDO** alterar asserções do oráculo. |
| **4. Arquiteto de Cristalização** | Decompõe a tripa verde em Domain, Application e Adapters. | `Domain/`, `Application/`, `Infrastructure/` | **PROIBIDO** alterar asserções dos testes de contrato; **PROIBIDO** violar regras do NetArchTest. |
| **5. Verificador Independente (QA)** | Executa testes adversariais, NetArchTest e emite o parecer final da fatia. | `tests/Architecture/`, relatórios de auditoria | **PROIBIDO** baixar thresholds de cobertura ou ignorar violações de arquitetura. |
| **6. Engenheiro de Plataforma & Cutover** | Mantém CI/CD, templates Bicep, YARP e executa o plano de transição. | `infra/`, `.github/`, configs de YARP, runbooks | **PROIBIDO** alterar regras de negócio nas camadas de domínio. |

---

## 2. Protocolo de Handoff entre Agentes

1. Nenhum agente avança de fase baseando-se em conversas em linguagem natural.
2. Cada transição exige a geração e validação de um arquivo `handoff.json` (conforme o schema em [`LIFECYCLE.md`](./LIFECYCLE.md)).
3. Se um agente encontrar uma regra de negócio ambígua no legado:
   - Ele **NÃO deve assumir uma decisão silenciosamente**.
   - Ele deve registrar o item no campo `open_questions` do `handoff.json` para validação pelo Arquiteto Humano.
