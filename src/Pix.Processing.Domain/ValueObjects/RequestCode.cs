namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct RequestCode
{
    public string Valor { get; }

    public RequestCode(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("RequestCode não pode ser vazio.", nameof(valor));

        if (valor.Length > 64)
            throw new ArgumentException("RequestCode não pode exceder 64 caracteres.", nameof(valor));

        Valor = valor.Trim();
    }

    public override string ToString() => Valor;

    public static implicit operator string(RequestCode code) => code.Valor;
    public static explicit operator RequestCode(string valor) => new(valor);
}
