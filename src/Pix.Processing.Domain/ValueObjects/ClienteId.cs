namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct ClienteId
{
    public string Valor { get; }

    public ClienteId(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("ClienteId não pode ser vazio ou nulo.", nameof(valor));

        Valor = valor.Trim();
    }

    public override string ToString() => Valor;

    public static implicit operator string(ClienteId id) => id.Valor;
    public static explicit operator ClienteId(string valor) => new(valor);
}
