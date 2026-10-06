using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Domain.Entities;

public class HistoricoExecucao
{
    public EndToEndId EndToEndId { get; init; }
    public Guid PedidoId { get; init; }
    public string Status { get; init; } = "Processado";
    public DateTimeOffset ExecutadoEm { get; init; } = DateTimeOffset.UtcNow;
}
