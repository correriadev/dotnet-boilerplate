using System.Data;
using Pix.Processing.Domain.Entities;

namespace Pix.Processing.Application.Ports;

public interface IOutboxRepository
{
    Task GravarAsync(MensagemOutbox mensagem, IDbTransaction? tx = null, CancellationToken ct = default);
    Task<IReadOnlyList<MensagemOutbox>> AdquirirLotePendenteAsync(int batchSize, IDbTransaction? tx = null, CancellationToken ct = default);
    Task<MensagemOutbox?> ObterPorEventIdAsync(string eventId, IDbTransaction? tx = null, CancellationToken ct = default);
    Task<int> ExpurgaPublicadosAsync(DateTimeOffset antesDe, int batchSize = 1000, IDbTransaction? tx = null, CancellationToken ct = default);
}
