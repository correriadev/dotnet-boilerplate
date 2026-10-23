# Product Backlog Building (PBB) — Esteira de Entrega Contínua e Governança Multiambiente

## 1. Product

* **Nome do Produto:** Esteira de Entrega Contínua e Governança Multiambiente (Multi-Environment Delivery Pipeline) para a Plataforma Pix.
* **Objetivo:** Permitir que as equipes de engenharia entreguem incrementos de software de forma rápida, previsível e auditável através de múltiplos ambientes isolados (Desenvolvimento, Homologação e Produção), mitigando riscos operacionais e assegurando conformidade regulatória e alta disponibilidade em transações financeiras críticas.
* **Fronteiras do Produto:** Abrange o ciclo de empacotamento imutável de artefatos, governança de integridade de banco de dados, promoção de versões com portas de qualidade (Quality Gates) baseadas em métricas objetivas, estratégias controladas de implantação (Blue-Green em DEV/HOM e Canary em Produção), atualização resiliente de workers assíncronos e reversão determinística (Rollback). Não abrange as regras de negócio intrínsecas às transações do ecossistema Pix ou o provisionamento inicial de redes e clusters.
* **Intenção do Produto:** Estabelecer uma esteira de entrega orientada a evidências ("Build Once, Deploy Many"), assegurando que exatamente o mesmo byte auditado e validado em estágios inferiores seja promovido para produção com zero confiança implícita.

---

## 2. Problems

* **P01 — Divergência entre artefatos testados e implantados:** Recompilar o código-fonte em cada ambiente introduz o risco de comportamentos divergentes entre homologação e produção decorrentes de variações em tempo de build, cache ou dependências transitórias.
* **P02 — Falhas em migrações de banco concorrentes ou acopladas ao deploy:** Execuções de scripts de banco realizadas sem exclusão mútua ou embutidas na inicialização das instâncias geram concorrência descontrolada, falhas parciais e serviços rodando em schemas inconsistentes.
* **P03 — Promoção empírica ou desprovida de evidências objetivas de qualidade:** Promover versões entre ambientes baseando-se apenas em julgamento subjetivo ou testes superficiais permite que regressões de desempenho, violações de SLO e anomalias de latência cheguem a produção.
* **P04 — Risco de impacto generalizado a clientes em novos releases:** Direcionar 100% dos usuários imediatamente para uma nova versão expõe toda a base operacional a incidentes críticos e indisponibilidade caso ocorram falhas em tempo de execução.
* **P05 — Recuperação lenta, imprecisa ou insegura em incidentes de produção:** Processos de rollback que dependem de nova compilação, ou que desconsideram o estado dos dados já gravados pela versão candidata, geram tempo prolongado de indisponibilidade e risco de perda de integridade transacional.
* **P06 — Exposição e vazamento de credenciais privilegiadas:** Manter segredos de longa duração, senhas fixas em variáveis de CI/CD ou credenciais compartilhadas entre ambientes quebra o princípio de menor privilégio e eleva o risco de invasão ou vazamento de dados produtivos.
* **P07 — Vulnerabilidades e violação de integridade na cadeia de suprimentos (Supply Chain):** Consumir dependências externas ou publicar contêineres sem inventário estruturado (SBOM), varreduras de vulnerabilidade e selagem criptográfica expõe a esteira a artefatos adulterados ou maliciosos.
* **P08 — Feedback tardio de integração e quebra silenciosa de dependências:** Falta de validação automática e imediata em ambiente de desenvolvimento impede que desenvolvedores identifiquem regressões de integração de banco, brokers e caches logo após a integração do código.

---

## 3. Expectations

* **E01 — Garantia de imutabilidade dos artefatos (Build Once, Deploy Many):** O binário exato, assinado digitalmente e testado em desenvolvimento e homologação deve ser o mesmo promovido para produção, associado a um identificador criptográfico único (digest SHA256).
* **E02 — Sincronismo, exclusão mútua e compatibilidade na governança de dados:** As migrações de banco de dados devem executar em processo dedicado com lock distribuído/exclusão mútua, suporte a reexecução idempotente e garantia de compatibilidade com versões N-1 ativas antes da liberação de tráfego.
* **E03 — Quality gates automatizados e orientados a SLOs com evidências positivas:** A progressão entre ambientes deve exigir comprovação objetiva de estabilidade funcional e cumprimento de metas de serviço (ex: latência P95 e taxa de erros sob carga representativa), adotando postura *Fail-Closed* em caso de falha de telemetria.
* **E04 — Exposição gradual e supervisionada de tráfego em produção (Canary):** Novos lançamentos de APIs devem expor inicialmente apenas uma fração controlada de requisições, exigindo evidência de amostragem mínima sem incidentes antes da liberação total de tráfego.
* **E05 — Reversão rápida, determinística e com salvaguarda de dados:** Em caso de anomalia, o tráfego deve retornar à versão estável precedente em tempo delimitado, com verificação de compatibilidade do schema ativo e procedimento explícito de contingência caso o rollback seja inseguro.
* **E06 — Isolamento Zero-Trust e privilégio mínimo por ambiente:** Cada ambiente deve operar exclusivamente com suas próprias identidades federadas temporárias (OIDC), com separação estrita entre papéis de deploy de aplicação e execução de migração de banco.
* **E07 — Rastreabilidade e conformidade da cadeia de suprimentos:** Todos os componentes do release devem conter inventário de dependências (SBOM) e atestação de segurança contra vulnerabilidades conhecidas antes de qualquer promoção.
* **E08 — Feedback contínuo e rápido com testes de prontidão profunda:** O ambiente de desenvolvimento deve validar imediatamente a saúde e conectividade real de todas as dependências de infraestrutura a cada deploy.

---

## 4. Personas

### Persona 1: Engenheiro de Software / Desenvolvedor
* **Objetivo Primário:** Entregar novas funcionalidades e correções de bugs de forma fluida, recebendo feedback imediato sobre a saúde e compatibilidade do seu código.
* **Interações Relevantes:** Submissão de código para integração, acompanhamento de testes de prontidão em DEV e garantia de retrocompatibilidade de dados (N-1).
* **Problemas e Expectativas Vinculados:** P01, P02, P08 / E01, E02, E08.

### Persona 2: Engenheiro de Plataforma / DevOps / SRE
* **Objetivo Primário:** Assegurar a confiabilidade e previsibilidade da infraestrutura de entrega, padronizar o ciclo de lançamentos e automatizar barreiras de qualidade e recuperação operacional.
* **Interações Relevantes:** Gestão de esteiras de automação, configuração de quality gates de SLOs, monitoramento de canaries e acionamento de rollbacks.
* **Problemas e Expectativas Vinculados:** P03, P04, P05 / E03, E04, E05.

### Persona 3: Tech Lead / Release Manager
* **Objetivo Primário:** Ter governança e visibilidade completa sobre o que entra em produção, autorizando liberações com base em evidências consolidadas de conformidade técnica e operacional.
* **Interações Relevantes:** Avaliação de relatórios consolidados de gates, auditoria de manifestos e aprovação formal em janelas de produção.
* **Problemas e Expectativas Vinculados:** P03, P06, P07 / E03, E06, E07.

### Persona 4: Auditor de Segurança / SecOps
* **Objetivo Primário:** Garantir a conformidade regulatória, a integridade da cadeia de suprimentos (Supply Chain) e a inexistência de credenciais compartilhadas ou permissões excessivas.
* **Interações Relevantes:** Auditoria de SBOM e proveniência de contêineres, verificação de scans de vulnerabilidade e inspeção de políticas de menor privilégio e OIDC.
* **Problemas e Expectativas Vinculados:** P06, P07 / E06, E07.

---

## 5. Functionalities

* **F01 — Empacotamento Centralizado e Selagem de Release Imutável:** Compila os serviços uma única vez, gera manifestos de versão com digests SHA256 imutáveis, atestações SBOM e assinaturas digitais para promoção segura.
* **F02 — Execução Controlada e Concorrente-Segura de Migrações de Dados:** Orquestra a execução isolada de migrações estruturais de banco com exclusão mútua, validação de integridade por checksum e garantia de convivência N-1 (Expand/Contract).
* **F03 — Promoção e Validação de Prontidão Contínua em DEV:** Implanta automaticamente os artefatos selados em ambiente de desenvolvimento e valida a prontidão funcional profunda de todas as dependências conectadas.
* **F04 — Homologação com Quality Gates Automatizados baseados em SLOs:** Conduz validações integradas em homologação, exigindo cumprimento estrito de metas de latência e taxas de erro sob carga representativa com política *Fail-Closed*.
* **F05 — Implantação Progressiva (Canary) e Supervisionada em Produção:** Realiza o rollout gradual de tráfego de APIs em produção condicionado à amostragem mínima e ausência de violação de SLOs durante janela de observação.
* **F06 — Atualização Segura e Graceful Shutdown de Consumidores Assíncronos (Workers):** Gerencia a transição de versão de workers que processam filas, garantindo drenagem de mensagens em voo, renovação de locks e execução idempotente.
* **F07 — Recuperação Determinística por Rollback e Contingência de Dados:** Executa a reversão controlada de tráfego para a revisão estável anterior com checagem prévia de compatibilidade de dados e roteiro explícito para contingências de dados incompatíveis.
* **F08 — Gestão Segregada de Credenciais e Acessos Zero-Trust:** Assegura que cada ambiente utilize identidades federadas efêmeras e cofres de segredos isolados, com segregação estrita de privilégios entre papéis de migração e aplicação.

---

## 6. Product Backlog

### PBI01 — Empacotamento centralizado e geração de manifesto de release imutável
* **Como** *Engenheiro de Software*,
* **Quero** que as imagens e manifestos de componentes sejam gerados e selados uma única vez por versão integrada,
* **Para que** o mesmo binário testado seja promovido entre ambientes sem discrepâncias decorrentes de recompilação.
* **Critérios de Aceite:**
  * Compilação dos serviços e geração de imagens executadas uma única vez no fluxo de empacotamento.
  * Publicação de manifesto estruturado (`release-manifest.json`) contendo os digests SHA256 exatos de cada serviço.
  * Rejeição automática de qualquer promoção posterior que tente implantar tags mutáveis (ex: `latest`) ou imagens com digests divergentes do manifesto.
  * Rastreabilidade explícita no manifesto vinculando commit SHA, run ID do CI e configurações de implantação versionadas.

### PBI02 — Inspeção de segurança, inventário (SBOM) e assinatura da cadeia de suprimentos
* **Como** *Auditor de Segurança*,
* **Quero** que o pacote de release seja inspecionado quanto a vulnerabilidades, possua SBOM gerado e seja assinado digitalmente,
* **Para que** apenas artefatos íntegros, auditados e em conformidade com políticas de segurança possam ser implantados.
* **Critérios de Aceite:**
  * Bloqueio automático da esteira caso sejam detectadas vulnerabilidades com severidade Critical ou High não justificadas formalmente.
  * Geração e armazenamento do inventário de componentes de software (SBOM em formato SPDX) para cada imagem do pacote.
  * Assinatura digital do manifesto e das imagens de contêiner antes da disponibilização para os ambientes.
  * Verificação criptográfica da assinatura e integridade do pacote no início de cada estágio de promoção (DEV, HOM, PRD).

### PBI03 — Execução síncrona de migrações estruturais com exclusão mútua e idempotência
* **Como** *Engenheiro de Plataforma*,
* **Quero** que as migrações estruturais de banco executem em processo dedicado com barreira síncrona e exclusão mútua,
* **Para que** nenhuma aplicação inicie atendimento com schema desatualizado e múltiplos deploys não corrompam a estrutura do banco.
* **Critérios de Aceite:**
  * Execução da migração como tarefa finita e isolada, desvinculada do ciclo de vida dos contêineres de aplicação.
  * Mecanismo de exclusão mútua (lock de banco ou serialização estrita de esteira) impedindo execuções simultâneas contra a mesma base.
  * Verificação de checksums e execução estritamente idempotente de scripts já aplicados.
  * Bloqueio do avanço da esteira caso a tarefa de migração falhe ou exceda o tempo limite pré-estabelecido, mantendo a versão estável em atendimento.

### PBI04 — Salvaguarda de compatibilidade retroativa de schema (N-1) e política de contração
* **Como** *Engenheiro de Software*,
* **Quero** garantia de que as alterações de banco suportem a versão de código em execução sem locks destrutivos,
* **Para que** os serviços ativos não sofram falhas operacionais durante o período transitório de migração.
* **Critérios de Aceite:**
  * Validação prévia de que os scripts de migração seguem o padrão de expansão (aditivo, colunas anuláveis ou com defaults) sem quebra da versão N-1.
  * Adiamento de scripts destrutivos de contração (remoção de colunas/tabelas) até o encerramento formal da janela de reversão do release.
  * Teste automatizado de compatibilidade confirmando que a versão de aplicação estável em execução continua operando sem erros após a aplicação da migração.

### PBI05 — Implantação e verificação de prontidão funcional contínua em DEV
* **Como** *Engenheiro de Software*,
* **Quero** implantação automática e checagem de prontidão funcional profunda em DEV a cada nova versão aprovada,
* **Para que** a equipe obtenha feedback funcional rápido sobre a integração de ponta a ponta sem intervenção manual.
* **Critérios de Aceite:**
  * Disparo automático da implantação em DEV logo após a selagem do pacote de release aprovado.
  * Verificação de prontidão profunda (deep health check) confirmando com sucesso (`HTTP 200`) a conectividade real com Key Vault, Banco de Dados, Cache e Fila.
  * Execução de suite de testes de fumaça funcional validando fluxos básicos de API após a confirmação de prontidão.
  * Notificação imediata em caso de falha de inicialização ou indisponibilidade de componentes dependentes.

### PBI06 — Homologação com Quality Gate automatizado orientado a SLOs e tráfego sintético
* **Como** *Engenheiro de Plataforma*,
* **Quero** que a promoção em Homologação seja condicionada à aprovação automática por metas de serviço (SLOs) sob carga,
* **Para que** versões com degradação de desempenho ou taxas anormais de erro sejam barradas antes de produção.
* **Critérios de Aceite:**
  * Execução de carga sintética padronizada (mínimo de 1.000 requisições distribuídas em 5 minutos) contra o ambiente de homologação.
  * Avaliação automatizada de critérios objetivos de SLO: Latência P95 < 400ms e Taxa de Erro HTTP 5xx < 0.1%.
  * Adoção estrita da política *Fail-Closed*: caso o serviço de métricas/telemetria esteja indisponível, retorne erro/timeout ou a amostra seja inferior ao volume mínimo, a esteira é abortada automaticamente sem promoção.
  * Registro e arquivamento auditável do parecer de conformidade de SLOs vinculado ao pacote de release.

### PBI07 — Consolidação de evidências e governança de aprovação para Produção
* **Como** *Tech Lead*,
* **Quero** visualizar o histórico de conformidade de um release em um portal formal de aprovação,
* **Para que** a autorização de liberação para Produção ocorra com base em evidências técnicas consolidadas e dentro da janela permitida.
* **Critérios de Aceite:**
  * Exigência de aprovação manual por papéis com permissão formal no repositório antes de iniciar a promoção em Produção.
  * Disponibilização centralizada dos relatórios de testes funcionais, varredura de segurança (Trivy/SBOM) e parecer de SLO de Homologação.
  * Bloqueio absoluto de promoção para Produção caso as etapas de DEV e Homologação não tenham sido concluídas com aprovação integral.

### PBI08 — Implantação progressiva Canary em Produção com validação de tráfego mínimo
* **Como** *Engenheiro de Plataforma*,
* **Quero** direcionar inicialmente uma fração minoritária de tráfego para a nova versão de APIs em produção sob monitoramento contínuo de SLOs,
* **Para que** o raio de impacto de eventuais falhas seja restrito e a promoção a 100% exija evidência positiva de operação real.
* **Critérios de Aceite:**
  * Roteamento de fração controlada de tráfego (10%) para a revisão candidata, mantendo a revisão estável no atendimento da maioria (90%).
  * Janela fixa de observação (Bake Window) de 10 minutos com monitoramento ativo de taxas de erro e latência específicas da revisão candidata comparadas à versão estável.
  * Critério de sucesso condicionado à presença de amostra mínima representativa (mínimo de 500 requisições recebidas pela candidata) sem violação de limites de SLO (Latência P95 < 400ms, Taxa de Erro HTTP 5xx < 0.1%).
  * Aplicação da política *Fail-Closed*: caso o serviço de telemetria fique indisponível ou a amostra de requisições seja insuficiente ao término da janela, a promoção a 100% é bloqueada e o tráfego canary é imediatamente revertido.
  * Comutação automática para 100% de tráfego somente após o encerramento da janela com todas as metas de serviço aprovadas.

### PBI09 — Atualização segura e Graceful Shutdown de Workers de mensageria
* **Como** *Engenheiro de Plataforma*,
* **Quero** que os componentes consumidores de filas (workers) sejam atualizados com desligamento gracioso e processamento idempotente,
* **Para que** nenhuma mensagem em processamento seja perdida ou corrompida durante a transição de versão.
* **Critérios de Aceite:**
  * Operação dos workers em modo de revisão única ativa, isolando o ciclo de atualização das regras de tráfego HTTP.
  * Interrupção imediata da leitura de novas mensagens na fila ao receber sinal `SIGTERM`.
  * Configuração de período de tolerância de término gracioso (*termination grace period*) de 90 segundos, garantindo tempo hábil para drenar mensagens em voo antes da desativação da réplica.
  * Renovação ativa de locks distribuídos no Redis durante o processamento de lote para evitar aquisição concorrente por outra réplica.
  * Deduplicação transacional estrita no banco relacional via chave idempotente única por transação Pix, prevenindo duplicidade contábil mesmo em caso de reentrega.

### PBI10 — Recuperação determinística por Rollback de tráfego
* **Como** *Engenheiro de Plataforma*,
* **Quero** reverter o tráfego de produção instantaneamente para a revisão estável anterior diante de anomalias,
* **Para que** o tempo de restabelecimento do serviço crítico (MTTR) seja mínimo e sem necessidade de novo ciclo de compilação.
* **Critérios de Aceite:**
  * Submissão obrigatória de toda e qualquer reversão (automática ou manual) à verificação prévia de integridade de dados (PBI11).
  * Capacidade de reorientar 100% do tráfego das APIs para a revisão estável anterior em poucos segundos através de comando determinístico de tráfego (`az containerapp ingress traffic set`).
  * Acionamento automático do rollback caso ocorram anomalias críticas ou violação de SLOs durante a janela de observação do Canary.
  * Disponibilização de fluxo manual e controlado de rollback para atuação imediata do time de operações em caso de incidentes tardios.

### PBI11 — Salvaguarda de integridade de dados e contingência no Rollback
* **Como** *Engenheiro de Plataforma*,
* **Quero** validação prévia de compatibilidade do schema de banco e diretrizes claras de contingência antes do rollback de código,
* **Para que** a versão restaurada não encontre inconsistências estruturais ou dados inválidos gerados pela versão candidata.
* **Critérios de Aceite:**
  * Verificação automatizada de que o schema ativo do banco é compatível com a revisão de código anterior que se deseja restaurar.
  * Verificação de que registros e transações já gravados pela versão candidata durante o período de canary não contenham formatos que quebrem os parsers da versão anterior.
  * Bloqueio do procedimento automático de reversão com emissão de alerta operacional caso o rollback de código apresente risco de quebra de integridade de dados.
  * Definição de procedimento documentado de contingência operacional (modo de manutenção temporário com isolamento de rota e acionamento de hotfix direto) para casos em que o rollback simples for considerado inseguro.

### PBI12 — Autenticação federada efêmera (OIDC) e isolamento Zero-Trust de identidades
* **Como** *Auditor de Segurança*,
* **Quero** que a esteira utilize identidades temporárias federadas (OIDC) e segregadas por ambiente,
* **Para que** nenhuma credencial estática de longa duração exista no repositório e o privilégio seja estritamente delimitado.
* **Critérios de Aceite:**
  * Autenticação com o provedor de nuvem realizada exclusivamente via OIDC com tokens efêmeros de curta duração.
  * Segregação de identidades: a identidade de DEV não possui autorizações em HOM ou PRD.
  * Separação estrita de privilégios: a identidade de execução de deploy da aplicação não possui permissão para alterar estruturas de banco de dados ou políticas de segurança.
  * Teste negativo automatizado validando que tentativas de autenticação com contexto/branch incorreto são sumariamente rejeitadas.
  * Teste negativo automatizado validando que a identidade de DEV não possui permissão para leitura de segredos no Key Vault de PRD.

### PBI13 — Segregação física e lógica de cofres de segredos por ambiente
* **Como** *Engenheiro de Plataforma*,
* **Quero** que cada ambiente de esteira acesse exclusivamente seu respectivo cofre de segredos,
* **Para que** segredos produtivos permaneçam inacessíveis a rotinas de desenvolvimento e homologação.
* **Critérios de Aceite:**
  * Vinculação estrita de cada ambiente da esteira (DEV, HOM, PRD) ao seu respectivo Key Vault isolado.
  * Impossibilidade técnica de leitura de segredos de PRD a partir de identidades ou rotinas associadas a DEV/HOM.

---

## 7. Traceability

| Problema (P) | Expectativa (E) | Persona | Funcionalidade (F) | PBIs Derivados | Evidência de Aceite |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **P01** (Divergência de artefatos) | **E01** (Imutabilidade de artefatos) | Engenheiro de Software | **F01** (Empacotamento Centralizado) | **PBI01** | Manifesto `release-manifest.json` com digests SHA256 idênticos em todos os deploys. |
| **P07** (Ameaça a Supply Chain) | **E07** (Rastreabilidade e SBOM) | Auditor de Segurança | **F01** (Empacotamento Centralizado) | **PBI02** | Assinatura digital validada via Cosign e relatório Trivy sem itens Critical/High. |
| **P02** (Falhas concorrentes de banco) | **E02** (Sincronismo e isolamento) | Engenheiro de Plataforma | **F02** (Migrações Concorrente-Seguras) | **PBI03** | Task de migração com lock executada e finalizada com status terminal antes do deploy. |
| **P02** (Falhas concorrentes de banco) | **E02** (Compatibilidade N-1) | Engenheiro de Software | **F02** (Migrações Concorrente-Seguras) | **PBI04** | Teste de compatibilidade N-1 aprovado sem quebra da versão estável em execução. |
| **P08** (Feedback tardio de integração) | **E08** (Feedback contínuo DEV) | Engenheiro de Software | **F03** (Promoção Contínua em DEV) | **PBI05** | Deep Health Check (HTTP 200) em Key Vault, SQL, Redis e Service Bus validado em DEV. |
| **P03** (Promoção empírica) | **E03** (Quality Gates orientados a SLOs) | Engenheiro de Plataforma | **F04** (Homologação com Quality Gates) | **PBI06** | Parecer de SLOs (P95 < 400ms, erros < 0.1%) sob carga representativa com Fail-Closed. |
| **P03** (Promoção empírica) | **E03** (Governança e evidências) | Tech Lead | **F04** (Homologação com Quality Gates) | **PBI07** | Registro formal de aprovação no GitHub Environment contendo links de todas as evidências. |
| **P04** (Risco de impacto generalizado) | **E04** (Exposição gradual Canary) | Engenheiro de Plataforma | **F05** (Implantação Canary em PRD) | **PBI08** | Divisão de tráfego 90/10 com Bake Window concluída e amostragem mínima aprovada. |
| **P02**, **P04** | **E02**, **E04** | Engenheiro de Plataforma | **F06** (Atualização Segura de Workers) | **PBI09** | Worker operando em Single Revision com draining gracioso e sem perda de mensagens. |
| **P05** (Recuperação lenta / insegura) | **E05** (Reversão determinística) | Engenheiro de Plataforma | **F07** (Recuperação por Rollback) | **PBI10** | Tráfego comutado para a revisão anterior em poucos segundos mantendo estabilidade. |
| **P05** (Recuperação lenta / insegura) | **E05** (Salvaguarda de dados) | Engenheiro de Plataforma | **F07** (Recuperação por Rollback) | **PBI11** | Verificação de compatibilidade de schema executada e rota de contingência documentada. |
| **P06** (Exposição de credenciais) | **E06** (Isolamento Zero-Trust) | Auditor de Segurança | **F08** (Gestão Segregada de Acessos) | **PBI12** | Autenticação OIDC com menor privilégio e testes negativos de acesso cruzado aprovados. |
| **P06** (Exposição de credenciais) | **E06** (Isolamento de cofres) | Engenheiro de Plataforma | **F08** (Gestão Segregada de Acessos) | **PBI13** | Key Vaults isolados por ambiente sem vazamento de segredos produtivos para DEV/HOM. |

---

## 8. Assumptions

1. **[Premissa 01 — Validação Contínua em PR]:** Testes unitários, testes de arquitetura e validações de lint continuam sendo executados durante o ciclo de Pull Request antes do merge na branch principal. *Responsável: Tech Lead | Marco: CI de PR.*
2. **[Premissa 02 — Probes Profundos de Prontidão]:** Os serviços da aplicação expõem probes HTTP dedicados de prontidão/saúde que testam ativamente a conectividade real com suas dependências de infraestrutura (Key Vault, SQL, Redis, Service Bus). *Responsável: Engenheiro de Software | Marco: PBI05.*
3. **[Premissa 03 — Evolução Aditiva de Dados (Expand/Contract)]:** As alterações no banco de dados seguirão uma política de evolução aditiva em duas fases, garantindo que adições de colunas ou tabelas não quebrem a versão N-1 ativa, adiando exclusões destrutivas. *Responsável: Engenheiro de Software | Marco: PBI04.*
4. **[Premissa 04 — Desacoplamento da Infraestrutura Base]:** A infraestrutura base dos ambientes (Resource Groups, Redes, Cofres e Registries) é gerenciada em ciclo desacoplado do deploy contínuo das aplicações. *Responsável: Engenheiro DevOps | Marco: Governança Cloud.*
5. **[Premissa 05 — Ambientes Protegidos no Repositório]:** O repositório possui ambientes configurados no GitHub (`dev`, `homologation`, `production`) com suas respectivas restrições de proteção e revisores habilitados. *Responsável: Engenheiro DevOps | Marco: Setup de Repositório.*

---

## 9. Open Questions

### Q01 — Qual deve ser a tolerância a falhas na indisponibilidade do serviço de telemetria externa durante o Quality Gate em Homologação?

**Suggested answer:** Adotar estritamente a política *Fail-Closed*: caso o serviço de telemetria/SLO esteja inacessível ou retorne erro/timeout, a promoção para Produção deve ser abortada automaticamente.

**Rationale:** Em sistemas financeiros de missão crítica, a ausência de evidência positiva de estabilidade não pode ser presumida como aprovação. A política fail-closed protege o ambiente produtivo contra liberações às cegas.

**Resolved answer:** Adotar estritamente a política *Fail-Closed*: caso o serviço de telemetria/SLO esteja inacessível ou retorne erro/timeout, a promoção para Produção deve ser abortada automaticamente.

**Answered by:** human

**Status:** Human validated

* **Responsável:** Engenheiro de Plataforma (SRE)
* **Marco de Decisão:** PBI06 (Gate de Homologação)
* **Evidência Verificável:** Pipeline abortada com status `failure` ao simular timeout do endpoint de métricas.

---

### Q02 — Como garantir integridade e ausência de duplicidade durante a atualização de workers assíncronos que consomem filas?

**Suggested answer:** Os workers devem operar em modo de revisão única ativa com desligamento gracioso (*graceful shutdown* com `terminationGracePeriodSeconds` de 90s), associado a processamento estritamente idempotente com deduplicação de mensagens no banco via tabela relacional transacional com chave única.

**Rationale:** Mensagens em fila podem sofrer reentrega em falhas de rede ou shutdown. O desligamento gracioso combinado com idempotência de negócio em banco garante que reinicializações ou reentregas não gerem duplicidade de lançamentos financeiros.

**Resolved answer:** Os workers devem operar em modo de revisão única ativa com desligamento gracioso (*graceful shutdown* com `terminationGracePeriodSeconds` de 90s), associado a processamento estritamente idempotente com deduplicação de mensagens no banco via tabela relacional transacional com chave única.

**Answered by:** human

**Status:** Human validated

* **Responsável:** Engenheiro de Software / Arquiteto
* **Marco de Decisão:** PBI09 (Atualização de Workers)
* **Evidência Verificável:** Teste de injeção de `SIGTERM` durante consumo de fila sem descarte ou duplicidade de liquidação.

---

### Q03 — Qual a estratégia de contingência caso um Rollback em produção seja barrado por incompatibilidade de schema de banco?

**Suggested answer:** Acionar procedimento de contingência operacional: isolar a rota/recurso afetado com mensagem padronizada de indisponibilidade parcial e disparar imediatamente esteira de correção emergencial (*hotfix*) com prioridade máxima, documentando o incidente na auditoria.

**Rationale:** Se a reversão de código quebraria os dados devido a uma alteração irreversível de schema já aplicada, forçar o rollback corromperia a base. A mitigação mais segura é a contenção funcional pontual e correção imediata (Fix-Forward).

**Resolved answer:** Acionar procedimento de contingência operacional: isolar a rota/recurso afetado com mensagem padronizada de indisponibilidade parcial e disparar imediatamente esteira de correção emergencial (*hotfix*) com prioridade máxima, documentando o incidente na auditoria.

**Answered by:** human

**Status:** Human validated

* **Responsável:** Tech Lead & Engenheiro de Plataforma
* **Marco de Decisão:** PBI11 (Salvaguarda de Rollback)
* **Evidência Verificável:** Execução de simulação de rollback com schema bloqueado acionando o fluxo de alerta e contenção.
