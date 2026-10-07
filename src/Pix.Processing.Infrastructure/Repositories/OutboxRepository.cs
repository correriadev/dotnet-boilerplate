using System.Data;
using Dapper;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;

namespace Pix.Processing.Infrastructure.Repositories;

public class OutboxRepository : IOutboxRepository
{
    private readonly IDbConnection _connection;

    public OutboxRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task GravarAsync(MensagemOutbox mensagem, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [pix].[OutboxRecords] (
                [EventId],
                [Tipo],
                [SchemaVersion],
                [CorrelationId],
                [PayloadJson],
                [Status],
                [OccurredAt],
                [CriadoEm],
                [PublishedAt],
                [LockOwner],
                [LockedUntil],
                [TraceParent],
                [TraceState]
            ) VALUES (
                @EventId,
                @Tipo,
                @SchemaVersion,
                @CorrelationId,
                @PayloadJson,
                @Status,
                @OccurredAt,
                @CriadoEm,
                @PublishedAt,
                @LockOwner,
                @LockedUntil,
                @TraceParent,
                @TraceState
            )";

        var parameters = new
        {
            mensagem.EventId,
            mensagem.Tipo,
            mensagem.SchemaVersion,
            mensagem.CorrelationId,
            mensagem.PayloadJson,
            mensagem.Status,
            mensagem.OccurredAt,
            mensagem.CriadoEm,
            mensagem.PublishedAt,
            mensagem.LockOwner,
            mensagem.LockedUntil,
            TraceParent = mensagem.TraceParent ?? System.Diagnostics.Activity.Current?.Id,
            TraceState = mensagem.TraceState ?? System.Diagnostics.Activity.Current?.TraceStateString
        };

        var command = new CommandDefinition(sql, parameters, transaction: tx, cancellationToken: ct);
        await _connection.ExecuteAsync(command);
    }

    public async Task<IReadOnlyList<MensagemOutbox>> AdquirirLotePendenteAsync(int batchSize, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@BatchSize)
                [EventId],
                [Tipo],
                [SchemaVersion],
                [CorrelationId],
                [PayloadJson],
                [Status],
                [OccurredAt],
                [CriadoEm],
                [PublishedAt],
                [LockOwner],
                [LockedUntil],
                [TraceParent],
                [TraceState]
            FROM [pix].[OutboxRecords] WITH (UPDLOCK, READPAST)
            WHERE [Status] = 'PENDING'
            ORDER BY [CriadoEm] ASC";

        var command = new CommandDefinition(sql, new { BatchSize = batchSize }, transaction: tx, cancellationToken: ct);
        var resultados = await _connection.QueryAsync<MensagemOutbox>(command);
        return resultados.AsList();
    }

    public async Task<MensagemOutbox?> ObterPorEventIdAsync(string eventId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                [EventId],
                [Tipo],
                [SchemaVersion],
                [CorrelationId],
                [PayloadJson],
                [Status],
                [OccurredAt],
                [CriadoEm],
                [PublishedAt],
                [LockOwner],
                [LockedUntil],
                [TraceParent],
                [TraceState]
            FROM [pix].[OutboxRecords]
            WHERE [EventId] = @EventId";

        var command = new CommandDefinition(sql, new { EventId = eventId }, transaction: tx, cancellationToken: ct);
        return await _connection.QuerySingleOrDefaultAsync<MensagemOutbox>(command);
    }

    public async Task<int> ExpurgaPublicadosAsync(DateTimeOffset antesDe, int batchSize = 1000, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            DELETE TOP (@BatchSize)
            FROM [pix].[OutboxRecords]
            WHERE [Status] = 'PUBLISHED'
              AND [PublishedAt] IS NOT NULL
              AND [PublishedAt] < @AntesDe";

        var command = new CommandDefinition(sql, new { BatchSize = batchSize, AntesDe = antesDe }, transaction: tx, cancellationToken: ct);
        return await _connection.ExecuteAsync(command);
    }
}
