using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Domain.Entities;

public class RegistroExecucao
{
    public EndToEndId EndToEndId { get; private set; }
    public Guid PedidoId { get; private set; }
    public ContaId ContaId { get; private set; }
    public long Sequencia { get; private set; }
    public string Owner { get; private set; }
    public long Generation { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IntencaoRegistrada { get; private set; }
    public ExternalEvidence? EvidenciaResultado { get; private set; }
    public EstadoPedidoPix Estado { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    // EF Constructor
    private RegistroExecucao() { Owner = null!; }

    public RegistroExecucao(
        EndToEndId endToEndId,
        Guid pedidoId,
        ContaId contaId,
        long sequencia,
        string owner,
        long generation,
        DateTimeOffset expiresAt,
        DateTimeOffset agora)
    {
        EndToEndId = endToEndId;
        PedidoId = pedidoId;
        ContaId = contaId;
        Sequencia = sequencia;
        Owner = owner;
        Generation = generation;
        Version = 1;
        ExpiresAt = expiresAt;
        IntencaoRegistrada = false;
        Estado = EstadoPedidoPix.EmProcessamento;
        CriadoEm = agora;
        AtualizadoEm = agora;
    }

    public void RegistrarIntencao(DateTimeOffset agora)
    {
        IntencaoRegistrada = true;
        Version++;
        AtualizadoEm = agora;
    }

    public void Concluir(ExternalEvidence evidencia, EstadoPedidoPix estadoFinal, DateTimeOffset agora)
    {
        EvidenciaResultado = evidencia;
        Estado = estadoFinal;
        Version++;
        AtualizadoEm = agora;
    }

    public void RenovarLease(string novoOwner, long novaGeneration, DateTimeOffset novoExpiresAt, DateTimeOffset agora)
    {
        Owner = novoOwner;
        Generation = novaGeneration;
        ExpiresAt = novoExpiresAt;
        Version++;
        AtualizadoEm = agora;
    }
}
