# Protocolo de Caracterização e Gestão do Oráculo

Este documento orienta a extração empírica de contratos do sistema legado e a criação de suítes de testes de caracterização determinísticas.

---

## 1. O Conceito de Oráculo de Testes

Um oráculo de testes é a fonte confiável da verdade comportamental. No contexto de migração de sistemas legados com IA:

> **O código legado é o oráculo empírico:** Não importa se a implementação do legado é elegante ou cheia de débitos técnicos. O comportamento produzido por ela em produção é o contrato vigente com clientes e integrações.

### A Consequência Completa da Operação
Comparar apenas o status HTTP (`200 OK`) e o payload retornado é um erro grave que oculta efeitos colaterais. O oráculo deve registrar e auditar as **5 dimensões da consequência completa**:

```
 ┌───────────────┐     ┌───────────────┐     ┌───────────────┐
 │   1. ENTRADA  │ ──► │2. ESTADO INIC.│ ──► │  3. RESPOSTA  │
 │ Payload/Auth  │     │ Dados no Banco│     │ Status/Body   │
 └───────────────┘     └───────────────┘     └───────┬───────┘
                                                     │
                       ┌───────────────┐             │
                       │5. EF. EXTERNOS│ ◄───────────┴──────────┐
                       │ Webhooks/Filas│                        │
                       └───────────────┘                        ▼
                                                     ┌──────────────────┐
                                                     │4. ESTADO MUTACION│
                                                     │ Tabelas alteradas│
                                                     └──────────────────┘
```

---

## 2. Passo a Passo para Extração do Oráculo (Tempo 1)

### Passo 1: Mapeamento de Dependências no Legado
1. Identificar o ponto de entrada (Controller, Soap Service, Message Handler).
2. Rastrear o caminho de persistência:
   - Queries SQL explícitas.
   - Stored Procedures invocadas.
   - Triggers disparadas nas tabelas afetadas.
3. Rastrear efeitos colaterais externos:
   - Escritas em diretórios de rede/arquivos.
   - Mensagens postadas em brokers (RabbitMQ, Service Bus, MSMQ).
   - Chamadas HTTP síncronas para outros sistemas.

### Passo 2: Geração de Fixtures Sanitizadas
1. Criar arquivos de mock de entrada (`fixtures/{caso}/request.json`).
2. Criar snapshots de resposta esperada (`fixtures/{caso}/expected_response.json`).
3. Criar snapshots de mutação de banco (`fixtures/{caso}/expected_db_diff.json`):
   ```json
   {
     "table": "TB_CLIENTE_SALDO",
     "where": "ID_CLIENTE = 12345",
     "columns_changed": {
       "VL_SALDO_DISPONIVEL": -150.00,
       "DT_ULTIMA_MOVIMENTACAO": "$IGNORE_TIMESTAMP"
     }
   }
   ```
4. **Sanitização de PII:** Todo dado pessoal (CPF, e-mails reais, nomes) deve ser mascarado nas fixtures antes do commit no repositório.

### Passo 3: Normalização de Campos Voláteis
Para evitar falsos positivos no comparador diferencial:
- **Timestamps:** Normalizar com tokens de comparação aproximada ou ignorar datas de auditoria se não afetarem regras de negócio.
- **Identificadores Aleatórios (GUIDs):** Validar regex/formato de GUID em vez de igualdade exata.

---

## 4. O Teste de Sensibilidade do Oráculo

Antes de confiar no oráculo para orientar a implementação da nova fatia:
1. **Injetar Defeito Controlado:** Altere propositalmente uma asserção de banco ou um valor na fixture.
2. **Executar o Teste:** O teste DEVE falhar.
3. Se o teste continuar passando "verde", o harness é considerado **insensível / cego** e deve ser corrigido imediatamente.
