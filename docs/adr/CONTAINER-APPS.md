---
doc_type: adr
domain: runtime
stack: [Azure Container Apps, KEDA, Service Bus, ACR, Bicep]
node_id: "adr:container-apps"
tags: [azure, container-apps, keda, scaling, managed-identity]
edges:
  - relation: references
    target: "adr:architecture"
updated: 2026-10-04
---
# Runtime Azure Container Apps e KEDA

## OVERVIEW

**Proposta de destino de F002**, ainda não provisionada. Azure Container Apps oferece escala baseada em KEDA por regras do próprio serviço; use Bicep/ARM, sem instalar operador KEDA nem aplicar CRDs Kubernetes. A versão upstream não garante suporte de todos os parâmetros no serviço gerenciado. [Escala ACA](https://learn.microsoft.com/en-us/azure/container-apps/scale-app).

## TOPOLOGY

REQUIRED: Separe dev/prod em resource groups, Container Apps Environments, bancos, namespaces e segredos distintos. Use ACR compartilhado com acesso segregado para promover o mesmo digest, ou importe o mesmo digest e valide igualdade entre registries. Avalie workload profiles e rede conforme custo/carga; não assuma HA apenas pelo número de réplicas.

| Recurso | Desenho proposto |
| --- | --- |
| Cinco Container Apps | Dois hosts HTTP e três workers existentes; resource limits por host |
| PostgreSQL | Serviço gerenciado, TLS, backup/PITR e migration controlada; sem EF InMemory cloud |
| Redis | Serviço gerenciado compatível; validar oferta/SKU vigente antes da implementação |
| Service Bus Premium | Sessions para comandos por conta, DLQ e retries; validar custo e disponibilidade regional |
| ACR | Pull via managed identity; desabilitar credenciais admin |
| Key Vault | Segredos externos com referência e identidade; sem valores em workflow/IaC/logs |
| Observabilidade | Logs, traces e métricas por release/revisão, sem payload financeiro sensível |

REQUIRED: Use managed identities por responsabilidade, `AcrPull` para registries com RBAC clássico ou role de leitura de repositório equivalente se ABAC estiver habilitado. Use Sender para publisher, Receiver para consumer/scaler conforme requisitos do serviço e acesso a segredos estritamente necessário. Teste permissões do scaler separadamente das do processo. [Pull por identidade](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity-image-pull).

REQUIRED: Defina ingress e TLS apenas para APIs; workers sem ingress público. Restrinja dados por rede/DNS e controle egress do parceiro/webhooks. Se ACR/serviços forem privados, forneça runner de deploy/testes com conectividade privada; OIDC não resolve acesso de rede.

## SCALING POLICY

Valores iniciais abaixo são **hipóteses de homologação**, sujeitos a teste de carga, latência, sessões ativas e orçamento. Não são SLOs aprovados.

| Host | Gatilho proposto | dev min/max | prod inicial min/max |
| --- | --- | --- | --- |
| Pix.CashOut.Api | HTTP, concorrência inicial 20 | 0/2 | 2/4 |
| Pix.Payment.Api | HTTP, concorrência inicial 20 | 0/2 | 2/4 |
| Outbank.Connector | `azure-servicebus` após integração real | 0/2 | 1/4 |
| Pix.Reconciliation.Worker | Polling DB existente; revisão única | 1/1 | 1/1 |
| Webhook.Dispatcher | Polling DB existente; revisão única | 1/1 | 1/1 |

REQUIRED: Para Outbank, declare `namespace`, `queueName` de fila session-enabled (ou `topicName` + `subscriptionName`), `messageCount: "5"`, `activationMessageCount: "0"` e identidade da regra. Use polling inicial 30 s e cooldown 300 s se expostos pela API ACA escolhida. Cooldown trata retorno a zero, não protege toda redução de réplicas. [ACA](https://learn.microsoft.com/en-us/azure/container-apps/scale-app), [scaler Service Bus](https://keda.sh/docs/2.18/scalers/azure-service-bus/).

REQUIRED: Crie fila/subscription e roles antes do scaler. Confirme schema ARM estável e parâmetros suportados no ambiente; não copie `ScaledObject` ou `TriggerAuthentication` diretamente para ACA.

REQUIRED: Não aplique escala por Service Bus aos loops atuais que consultam PostgreSQL. Enquanto não houver integração e validação, use Outbank 1/1 em ambiente isolado de estudo, sem considerá-lo apto a produção. Considere ACA Job agendado para reconciliação apenas após tornar o processo finito, exclusivo e idempotente.

### Ordering and resource budgets

REQUIRED: Consuma comandos com `ServiceBusSessionProcessor`, `SessionId` igual à conta canônica e `MaxConcurrentCallsPerSession=1` inicialmente. Sessions serializam o broker, mas não substituem sequência financeira, idempotência, ownership/fencing e commit transacional. [Sessions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sessions).

REQUIRED: Limite sessões concorrentes, prefetch e renovação de locks pelo tempo de execução. O scaler mede backlog, não quantidade de contas independentes: uma conta com backlog grande não permite paralelizar suas operações. Teste contas quentes e muitas sessões.

REQUIRED: Dimensione `maxReplicas × concorrência por réplica` dentro do orçamento Outbank, pool de conexões PostgreSQL e capacidade Redis. Implemente limite compartilhado entre réplicas; um limite local multiplicaria chamadas ao parceiro durante escala.

REQUIRED: Use outbox relay durável como pré-requisito explícito: defina host publisher (novo host ou hosted service com lease exclusivo), payload fiel, MessageId estável e publicação at-least-once. Não assuma que escalar consumers drena outbox ainda no banco. A decisão de host deve preceder sua imagem/IaC.

## REVISIONS AND HEALTH

REQUIRED: Para APIs, use multiple revisions, labels para smoke e mudança de tráfego após readiness. Para workers, use single revision e teste sobreposição transitória de réplicas na atualização. Nunca use peso HTTP como mecanismo de canário de fila. [Blue/green](https://learn.microsoft.com/en-us/azure/container-apps/blue-green-deployment).

REQUIRED: Configure explicitamente startup, liveness e readiness; use porta 8080 nas APIs após validar binding da imagem. Liveness deve detectar processo travado sem reiniciar todas as réplicas por indisponibilidade de dependência. Readiness avalia dependências necessárias ao atendimento. Os endpoints da CashOut existem, mas não comprovam todas as dependências. Para workers, implemente health endpoint interno ou probes adequadas ao processo; não presuma porta HTTP existente. [Probes](https://learn.microsoft.com/en-us/azure/container-apps/health-probes).

REQUIRED: Ao shutdown, pare aquisição, conclua ou abandone trabalho com segurança e libere locks; alinhe timeout do host, termination grace e renovação de locks. Uma chamada financeira interrompida deve entrar em reconciliação, sem reenvio cego.

REQUIRED: Valide rotação de referências Key Vault e eventual necessidade de restart/revisão. Alterações de configuração compartilhada podem afetar revisão anterior; rollback de tráfego não garante rollback de segredos ou schema.

## ACCEPTANCE AND OPERATIONS

| Cenário | Resultado e evidência esperados |
| --- | --- |
| Outbank 0 → N → 0 em dev | Fila real acorda consumers, drena backlog e retorna a zero; registre tempos e contagens |
| Duas réplicas e mesma conta | Ordem financeira preservada e efeitos idempotentes com redelivery |
| Processo morre após efeito externo | Reconciliação recupera evidência sem segundo pagamento |
| Lock expira ou deploy sobrepõe workers | Ownership antigo não conclui; mensagem não some |
| Broker indisponível | Outbox permanece durável, volta a publicar sem perder evento |
| Poison message | Retry limitado, DLQ e alerta; replay auditado, sem loop infinito |
| Identidade sem role | Alerta de scaler/runtime; ausência de credencial alternativa permissiva |
| Carga com parceiro lento | Orçamento global respeitado; pools não saturam; backlog observável |
| Banco sem configuração | Startup cloud falha; nunca cria banco em memória |

REQUIRED: Monitore idade da mensagem mais antiga, backlog, DLQ, atraso da outbox, locks perdidos, erros do scaler, réplicas/restarts, latência/5xx e pedidos incertos. Defina limiares e janela de promoção antes da implantação produtiva; ensaie alerta e rollback em dev.

## OPEN DECISIONS

REQUIRED: Resolva subscription/tenant, região, repo/branch, SKU e quotas, budget mensal, exposição de APIs, rede privada, SLO/RTO/RPO, concorrência do parceiro e host do outbox. Registre valores em parâmetros por ambiente; nenhum desses valores foi fornecido/aprovado nesta revisão.

## REFERENCES

- [ARCHITECTURE.md](./ARCHITECTURE.md): Arquitetura base e invariantes de integração.
- [CICD.md](./CICD.md): Build, promoção, identidade de pipeline e rollback.
- [plataforma_pix.md](../feature/plataforma_pix.md): Domínio Pix modernizado e implementado.
