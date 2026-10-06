using System.Data;
using Dapper;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Infrastructure.Repositories;

public class HistoricoExecucaoRepository : IHistoricoExecucaoRepository
{
    private readonly IDbConnection _connection;

    public HistoricoExecucaoRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task VerificarEInserirAsync(HistoricoExecucao historico, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [pix].[ProcessedIds] (
                [EndToEndId],
                [PedidoId],
                [Status],
                [ExecutadoEm]
            ) VALUES (
                @EndToEndId,
                @PedidoId,
                @Status,
                @ExecutadoEm
            )";

        var parameters = new
        {
            EndToEndId = historico.EndToEndId.Valor,
            historico.PedidoId,
            historico.Status,
            historico.ExecutadoEm
        };

        var command = new CommandDefinition(sql, parameters, transaction: tx, cancellationToken: ct);
        await _connection.ExecuteAsync(command);
    }

    public async Task<bool> JaProcessadoAsync(EndToEndId endToEndId, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM [pix].[ProcessedIds]
            WHERE [EndToEndId] = @EndToEndId";

        var command = new CommandDefinition(sql, new { EndToEndId = endToEndId.Valor }, transaction: tx, cancellationToken: ct);
        var total = await _connection.ExecuteScalarAsync<int>(command);
        return total > 0;
    }
}
