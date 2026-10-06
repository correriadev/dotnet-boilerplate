using Pix.Processing.Domain.Exceptions;

namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct EndToEndId
{
    public string Valor { get; }

    public EndToEndId(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("EndToEndId não pode ser vazio ou nulo.", nameof(valor));

        Valor = valor.Trim();
    }

    public static EndToEndId Criar(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Trim().Length != 32)
            throw new DomainException($"EndToEndId deve conter exatamente 32 caracteres alfanuméricos. Valor fornecido: '{valor}'.");

        return new EndToEndId(valor.Trim());
    }

    public override string ToString() => Valor;

    public static implicit operator string(EndToEndId id) => id.Valor;
    public static explicit operator EndToEndId(string valor) => new(valor);
}
