using System.Data;
using Pix.Processing.Domain.Entities;

namespace Pix.Processing.Application.Ports;

public interface IPedidoPixRepository
{
    Task InserirAsync(PedidoPix pedido, IDbTransaction? tx = null, CancellationToken ct = default);
    Task<PedidoPix?> ObterPorIdAsync(Guid id, IDbTransaction? tx = null, CancellationToken ct = default);
}
