using System.Data;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Ports;

public interface IHistoricoExecucaoRepository
{
    Task VerificarEInserirAsync(HistoricoExecucao historico, IDbTransaction? tx = null, CancellationToken ct = default);
    Task<bool> JaProcessadoAsync(EndToEndId endToEndId, IDbTransaction? tx = null, CancellationToken ct = default);
}
