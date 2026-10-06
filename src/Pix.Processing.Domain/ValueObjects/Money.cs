using Pix.Processing.Domain.Exceptions;

namespace Pix.Processing.Domain.ValueObjects;

public sealed record Money
{
    public decimal Quantia { get; }
    public string Moeda { get; }

    private Money()
    {
        Moeda = "BRL";
    }

    public Money(decimal quantia, string moeda = "BRL")
    {
        if (string.IsNullOrWhiteSpace(moeda))
            throw new ArgumentException("Moeda não pode ser vazia.", nameof(moeda));

        if (quantia < 0)
            throw new ArgumentOutOfRangeException(nameof(quantia), "Quantia não pode ser negativa.");

        // Verifica escala máxima de 2 casas decimais (100 * quantia deve ser inteiro)
        if (decimal.Round(quantia, 2) != quantia)
            throw new ArgumentException("Quantia não pode ter mais de duas casas decimais.", nameof(quantia));

        Quantia = quantia;
        Moeda = moeda.Trim().ToUpperInvariant();
    }

    public static Money De(decimal quantia, string moeda = "BRL")
    {
        if (quantia <= 0)
            throw new DomainException($"Quantia monetária deve ser estritamente positiva (> 0). Valor fornecido: {quantia}.");

        return new Money(quantia, moeda);
    }

    public static Money Zero(string moeda = "BRL") => new(0m, moeda);

    public static Money operator +(Money a, Money b)
    {
        if (a.Moeda != b.Moeda)
            throw new InvalidOperationException($"Não é possível somar moedas distintas: {a.Moeda} e {b.Moeda}.");
        return new Money(a.Quantia + b.Quantia, a.Moeda);
    }

    public static Money operator -(Money a, Money b)
    {
        if (a.Moeda != b.Moeda)
            throw new InvalidOperationException($"Não é possível subtrair moedas distintas: {a.Moeda} e {b.Moeda}.");
        return new Money(a.Quantia - b.Quantia, a.Moeda);
    }

    public override string ToString() => $"{Quantia:F2} {Moeda}";
}
