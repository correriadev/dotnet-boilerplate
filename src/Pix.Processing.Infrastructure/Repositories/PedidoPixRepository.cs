using System.Data;
using Dapper;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Infrastructure.Repositories;

public class PedidoPixRepository : IPedidoPixRepository
{
    private readonly IDbConnection _connection;

    public PedidoPixRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task InserirAsync(PedidoPix pedido, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [pix].[Pedidos] (
                [Id],
                [ClienteId],
                [ContaId],
                [TipoOperacao],
                [EndToEndId],
                [Valor],
                [Moeda],
                [Estado],
                [Versao],
                [CriadoEm],
                [AtualizadoEm]
            ) VALUES (
                @Id,
                @ClienteId,
                @ContaId,
                @TipoOperacao,
                @EndToEndId,
                @Valor,
                @Moeda,
                @Estado,
                @Versao,
                @CriadoEm,
                @AtualizadoEm
            )";

        var parameters = new
        {
            Id = pedido.Id,
            ClienteId = pedido.ClienteId.Valor,
            ContaId = pedido.ContaId.Valor,
            TipoOperacao = pedido.TipoOperacao.ToString(),
            EndToEndId = pedido.EndToEndId?.Valor,
            Valor = pedido.Valor.Quantia,
            Moeda = pedido.Valor.Moeda,
            Estado = pedido.Estado.ToString(),
            Versao = pedido.Versao,
            CriadoEm = pedido.CriadoEm,
            AtualizadoEm = pedido.AtualizadoEm
        };

        var command = new CommandDefinition(sql, parameters, transaction: tx, cancellationToken: ct);
        await _connection.ExecuteAsync(command);
    }

    public async Task<PedidoPix?> ObterPorIdAsync(Guid id, IDbTransaction? tx = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                [Id],
                [ClienteId],
                [ContaId],
                [TipoOperacao],
                [EndToEndId],
                [Valor],
                [Moeda],
                [Estado],
                [Versao],
                [CriadoEm],
                [AtualizadoEm]
            FROM [pix].[Pedidos]
            WHERE [Id] = @Id";

        var command = new CommandDefinition(sql, new { Id = id }, transaction: tx, cancellationToken: ct);
        var row = await _connection.QuerySingleOrDefaultAsync<PedidoRow>(command);

        if (row == null)
            return null;

        return new PedidoPix(
            row.Id,
            new ClienteId(row.ClienteId),
            new ContaId(row.ContaId),
            Enum.Parse<TipoOperacaoPix>(row.TipoOperacao),
            string.IsNullOrWhiteSpace(row.EndToEndId) ? null : new EndToEndId(row.EndToEndId),
            new Money(row.Valor, row.Moeda),
            criadoEm: row.CriadoEm);
    }

    private sealed class PedidoRow
    {
        public Guid Id { get; set; }
        public string ClienteId { get; set; } = string.Empty;
        public string ContaId { get; set; } = string.Empty;
        public string TipoOperacao { get; set; } = string.Empty;
        public string? EndToEndId { get; set; }
        public decimal Valor { get; set; }
        public string Moeda { get; set; } = "BRL";
        public string Estado { get; set; } = string.Empty;
        public int Versao { get; set; }
        public DateTimeOffset CriadoEm { get; set; }
        public DateTimeOffset? AtualizadoEm { get; set; }
    }
}
