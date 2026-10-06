using FluentAssertions;
using Microsoft.Data.SqlClient;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Repositories;
using Xunit;

namespace Integration;

public class DapperRepositoryTests
{
    private const string ConnectionString = "Server=localhost,1433;Database=PixTestDb;User Id=sa;Password=YourStrong@Passw0rd!;TrustServerCertificate=True;";

    public DapperRepositoryTests()
    {
        // Garante que o schema e tabelas [pix] estão criados e migrados
        DbUpRunner.Migrar(ConnectionString);
    }

    [Fact]
    public async Task TX_012_Should_Gravar_Pedido_E_Outbox_Atomicamente_Via_Dapper_When_TX01_Executa()
    {
        // Given
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var pedidoRepo = new PedidoPixRepository(connection);
        var outboxRepo = new OutboxRepository(connection);

        var pedidoId = Guid.NewGuid();
        var endToEndId = EndToEndId.Criar($"E12345678202610051200{Guid.NewGuid():N}"[..32]);
        var pedido = new PedidoPix(
            pedidoId,
            new ClienteId("cli-tx01"),
            new ContaId("cnt-tx01"),
            TipoOperacaoPix.CashOut,
            endToEndId,
            Money.De(250.75m));

        var outbox = new MensagemOutbox
        {
            EventId = Guid.NewGuid().ToString(),
            Tipo = "CashOutSolicitadoEvento",
            SchemaVersion = "1.0",
            CorrelationId = Guid.NewGuid().ToString(),
            PayloadJson = "{\"pedidoId\":\"" + pedidoId + "\"}",
            Status = "PENDING",
            OccurredAt = DateTimeOffset.UtcNow,
            CriadoEm = DateTimeOffset.UtcNow
        };

        // When: Gravação atômica sob a mesma transação T-SQL
        await pedidoRepo.InserirAsync(pedido, transaction);
        await outboxRepo.GravarAsync(outbox, transaction);
        await transaction.CommitAsync();

        // Then: Ambos existem no banco
        var pedidoSalvo = await pedidoRepo.ObterPorIdAsync(pedidoId);
        pedidoSalvo.Should().NotBeNull();
        pedidoSalvo!.Id.Should().Be(pedidoId);
        pedidoSalvo.Valor.Quantia.Should().Be(250.75m);

        var outboxSalvo = await outboxRepo.ObterPorEventIdAsync(outbox.EventId);
        outboxSalvo.Should().NotBeNull();
        outboxSalvo!.EventId.Should().Be(outbox.EventId);
    }

    [Fact]
    public async Task TX_013_Should_Reverter_Pedido_E_Outbox_Juntos_When_Falha_Injetada_Na_Transacao()
    {
        // Given
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var pedidoRepo = new PedidoPixRepository(connection);
        var outboxRepo = new OutboxRepository(connection);

        var pedidoId = Guid.NewGuid();
        var pedido = new PedidoPix(
            pedidoId,
            new ClienteId("cli-rb"),
            new ContaId("cnt-rb"),
            TipoOperacaoPix.CashOut,
            EndToEndId.Criar($"E12345678202610051200{Guid.NewGuid():N}"[..32]),
            Money.De(50.00m));

        var outbox = new MensagemOutbox
        {
            EventId = Guid.NewGuid().ToString(),
            Tipo = "CashOutSolicitadoEvento",
            CorrelationId = Guid.NewGuid().ToString(),
            PayloadJson = "{\"pedidoId\":\"" + pedidoId + "\"}",
            Status = "PENDING",
            OccurredAt = DateTimeOffset.UtcNow,
            CriadoEm = DateTimeOffset.UtcNow
        };

        // When: Inserção do pedido seguida por erro simulado e rollback
        await pedidoRepo.InserirAsync(pedido, transaction);
        await outboxRepo.GravarAsync(outbox, transaction);
        await transaction.RollbackAsync();

        // Then: Nenhum dos registros deve existir
        var pedidoSalvo = await pedidoRepo.ObterPorIdAsync(pedidoId);
        pedidoSalvo.Should().BeNull();

        var outboxSalvo = await outboxRepo.ObterPorEventIdAsync(outbox.EventId);
        outboxSalvo.Should().BeNull();
    }

    [Fact]
    public async Task TX_014_Should_Adquirir_Lote_Outbox_Com_SkipLock_TSql_When_Multiplos_Workers_Concorrem()
    {
        // Given: Inserir múltiplos registros de outbox
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var outboxRepo = new OutboxRepository(connection);

        var eventIds = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            var evtId = Guid.NewGuid().ToString();
            eventIds.Add(evtId);
            await outboxRepo.GravarAsync(new MensagemOutbox
            {
                EventId = evtId,
                Tipo = "CashOutSolicitadoEvento",
                CorrelationId = Guid.NewGuid().ToString(),
                PayloadJson = "{}",
                Status = "PENDING",
                OccurredAt = DateTimeOffset.UtcNow,
                CriadoEm = DateTimeOffset.UtcNow.AddMilliseconds(i)
            });
        }

        // When: Dois workers concorrentes adquirindo lotes sob transações simultâneas com UPDLOCK, READPAST
        await using var conn1 = new SqlConnection(ConnectionString);
        await using var conn2 = new SqlConnection(ConnectionString);
        await conn1.OpenAsync();
        await conn2.OpenAsync();

        await using var tx1 = (SqlTransaction)await conn1.BeginTransactionAsync();
        await using var tx2 = (SqlTransaction)await conn2.BeginTransactionAsync();

        var repo1 = new OutboxRepository(conn1);
        var repo2 = new OutboxRepository(conn2);

        var task1 = repo1.AdquirirLotePendenteAsync(10, tx1);
        var task2 = repo2.AdquirirLotePendenteAsync(10, tx2);

        var lotes = await Task.WhenAll(task1, task2);

        await tx1.CommitAsync();
        await tx2.CommitAsync();

        var idsLote1 = lotes[0].Select(x => x.EventId).ToHashSet();
        var idsLote2 = lotes[1].Select(x => x.EventId).ToHashSet();

        // Then: Os dois lotes devem ser mutuamente exclusivos (disjuntos) - nenhum lock conflita ou lê duplicado
        idsLote1.Intersect(idsLote2).Should().BeEmpty("UPDLOCK e READPAST devem garantir que lotes concorrentes sejam disjuntos");
    }

    [Fact]
    public async Task TX_015_Should_Rejeitar_EndToEndId_Duplicado_When_Insercao_Concorre_Em_ProcessedIds()
    {
        // Given
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var historicoRepo = new HistoricoExecucaoRepository(connection);

        var e2e = EndToEndId.Criar($"E12345678202610051200{Guid.NewGuid():N}"[..32]);
        var historico = new HistoricoExecucao
        {
            EndToEndId = e2e,
            PedidoId = Guid.NewGuid(),
            Status = "Processado",
            ExecutadoEm = DateTimeOffset.UtcNow
        };

        // When 1: Primeira inserção deve ter sucesso
        await historicoRepo.VerificarEInserirAsync(historico);
        var jaProcessado = await historicoRepo.JaProcessadoAsync(e2e);
        jaProcessado.Should().BeTrue();

        // When 2: Segunda inserção concorrente com mesma PK deve falhar com SqlException 2627 (PK violation)
        var act = () => historicoRepo.VerificarEInserirAsync(historico);

        // Then
        await act.Should().ThrowAsync<SqlException>()
            .Where(ex => ex.Number == 2627 || ex.Number == 2601);
    }

    [Fact]
    public async Task TX_RW_004_Should_Expurgar_Mensagens_Publicadas_Antigas_Do_Outbox_Sem_Afetar_Pendentes()
    {
        // Given: Banco ativo e repositório Outbox
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var outboxRepo = new OutboxRepository(connection);

        var eventoPendente = new MensagemOutbox
        {
            EventId = Guid.NewGuid().ToString(),
            Tipo = "TestePendente",
            PayloadJson = "{}",
            Status = "PENDING",
            OccurredAt = DateTimeOffset.UtcNow,
            CriadoEm = DateTimeOffset.UtcNow
        };
        var eventoPublicadoAntigo = new MensagemOutbox
        {
            EventId = Guid.NewGuid().ToString(),
            Tipo = "TestePublicadoAntigo",
            PayloadJson = "{}",
            Status = "PUBLISHED",
            OccurredAt = DateTimeOffset.UtcNow.AddHours(-3),
            CriadoEm = DateTimeOffset.UtcNow.AddHours(-3),
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };
        var eventoPublicadoRecente = new MensagemOutbox
        {
            EventId = Guid.NewGuid().ToString(),
            Tipo = "TestePublicadoRecente",
            PayloadJson = "{}",
            Status = "PUBLISHED",
            OccurredAt = DateTimeOffset.UtcNow,
            CriadoEm = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow
        };

        await outboxRepo.GravarAsync(eventoPendente);
        await outboxRepo.GravarAsync(eventoPublicadoAntigo);
        await outboxRepo.GravarAsync(eventoPublicadoRecente);

        // When: Expurga mensagens publicadas com mais de 1 hora (housekeeping)
        var deletados = await outboxRepo.ExpurgaPublicadosAsync(DateTimeOffset.UtcNow.AddHours(-1), batchSize: 500);

        // Then 1: Pelo menos 1 registro antigo deve ser expurgado
        deletados.Should().BeGreaterOrEqualTo(1);

        // Then 2: Mensagem pendente permanece intacta
        var pendenteAindaExiste = await outboxRepo.ObterPorEventIdAsync(eventoPendente.EventId);
        pendenteAindaExiste.Should().NotBeNull();
        pendenteAindaExiste!.Status.Should().Be("PENDING");

        // Then 3: Mensagem publicada recente permanece intacta
        var recenteAindaExiste = await outboxRepo.ObterPorEventIdAsync(eventoPublicadoRecente.EventId);
        recenteAindaExiste.Should().NotBeNull();

        // Then 4: Mensagem publicada antiga foi deletada
        var antigoRemovido = await outboxRepo.ObterPorEventIdAsync(eventoPublicadoAntigo.EventId);
        antigoRemovido.Should().BeNull();
    }
}

