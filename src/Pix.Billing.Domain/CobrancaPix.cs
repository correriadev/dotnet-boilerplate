namespace Pix.Billing.Domain;

public readonly record struct CobrancaId
{
    public string Valor { get; }

    public CobrancaId(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("CobrancaId não pode ser vazio ou nulo.", nameof(valor));

        Valor = valor.Trim();
    }

    public override string ToString() => Valor;
    public static implicit operator string(CobrancaId id) => id.Valor;
    public static explicit operator CobrancaId(string valor) => new(valor);
}

public enum EstadoCobranca
{
    Solicitada = 1,
    EmGeracao = 2,
    Disponivel = 3,
    Recusada = 4,
    GeracaoIncerta = 5,
    EmTriagem = 6
}

public class CobrancaPix
{
    public CobrancaId Id { get; private set; }
    public Guid PedidoId { get; private set; }
    public decimal Valor { get; private set; }
    public EstadoCobranca Estado { get; private set; }
    public string? ArtefatoQr { get; private set; }
    public string? MotivoRecusa { get; private set; }
    public int Versao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    // Construtor EF
    private CobrancaPix() { }

    public CobrancaPix(CobrancaId id, Guid pedidoId, decimal valor, DateTimeOffset? criadoEm = null)
    {
        Id = id;
        PedidoId = pedidoId;
        Valor = valor;
        Estado = EstadoCobranca.Solicitada;
        Versao = 1;
        CriadoEm = criadoEm ?? DateTimeOffset.UtcNow;
        AtualizadoEm = CriadoEm;
    }

    public void IniciarGeracao(DateTimeOffset agora)
    {
        if (Estado != EstadoCobranca.Solicitada && Estado != EstadoCobranca.GeracaoIncerta)
            throw new InvalidOperationException($"Não é permitido iniciar geração a partir de {Estado}.");

        Estado = EstadoCobranca.EmGeracao;
        Versao++;
        AtualizadoEm = agora;
    }

    public void Disponibilizar(string artefatoQr, DateTimeOffset agora)
    {
        if (Estado == EstadoCobranca.Disponivel) return; // Idempotente
        if (Estado == EstadoCobranca.Recusada)
            throw new InvalidOperationException("Não é possível disponibilizar cobrança já recusada.");

        ArtefatoQr = artefatoQr;
        Estado = EstadoCobranca.Disponivel;
        Versao++;
        AtualizadoEm = agora;
    }

    public void Recusar(string motivo, DateTimeOffset agora)
    {
        if (Estado == EstadoCobranca.Recusada) return; // Idempotente
        if (Estado == EstadoCobranca.Disponivel)
            throw new InvalidOperationException("Não é possível recusar cobrança já disponível.");

        MotivoRecusa = motivo;
        Estado = EstadoCobranca.Recusada;
        Versao++;
        AtualizadoEm = agora;
    }

    public void MarcarGeracaoIncerta(string motivo, DateTimeOffset agora)
    {
        if (Estado != EstadoCobranca.EmGeracao)
            throw new InvalidOperationException($"Geração incerta só é permitida a partir de EmGeracao (atual: {Estado}).");

        Estado = EstadoCobranca.GeracaoIncerta;
        Versao++;
        AtualizadoEm = agora;
    }
}
