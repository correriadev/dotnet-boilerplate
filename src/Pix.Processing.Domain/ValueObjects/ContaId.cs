namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct ContaId
{
    public string Valor { get; }

    public ContaId(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("ContaId não pode ser vazio ou nulo.", nameof(valor));

        Valor = valor.Trim();
    }

    public override string ToString() => Valor;

    public static implicit operator string(ContaId id) => id.Valor;
    public static explicit operator ContaId(string valor) => new(valor);
}
