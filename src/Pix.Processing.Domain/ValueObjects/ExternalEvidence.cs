namespace Pix.Processing.Domain.ValueObjects;

public sealed record ExternalEvidence
{
    public string Operacao { get; }
    public string Tipo { get; }
    public string Fonte { get; }
    public string Referencia { get; }
    public DateTimeOffset ObservadoEm { get; }
    public bool Sucesso { get; }

    private ExternalEvidence()
    {
        Operacao = "";
        Tipo = "";
        Fonte = "";
        Referencia = "";
    }

    public ExternalEvidence(string operacao, string tipo, string fonte, string referencia, DateTimeOffset observadoEm, bool sucesso)
    {
        if (string.IsNullOrWhiteSpace(operacao))
            throw new ArgumentException("Operacao não pode ser vazia.", nameof(operacao));

        if (string.IsNullOrWhiteSpace(tipo))
            throw new ArgumentException("Tipo não pode ser vazio.", nameof(tipo));

        if (string.IsNullOrWhiteSpace(fonte))
            throw new ArgumentException("Fonte não pode ser vazia.", nameof(fonte));

        if (string.IsNullOrWhiteSpace(referencia))
            throw new ArgumentException("Referencia não pode ser vazia.", nameof(referencia));

        Operacao = operacao.Trim();
        Tipo = tipo.Trim();
        Fonte = fonte.Trim();
        Referencia = referencia.Trim();
        ObservadoEm = observadoEm;
        Sucesso = sucesso;
    }

    public override string ToString() => $"[Fonte={Fonte}, Tipo={Tipo}, Ref={Referencia}, Sucesso={Sucesso}]";
}
