namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct AccountSequence
{
    public ContaId ContaId { get; }
    public long Numero { get; }

    public AccountSequence(ContaId contaId, long numero)
    {
        if (numero <= 0)
            throw new ArgumentOutOfRangeException(nameof(numero), "Número de sequência deve ser positivo e maior que zero.");

        ContaId = contaId;
        Numero = numero;
    }

    public AccountSequence Proximo() => new(ContaId, Numero + 1);

    public override string ToString() => $"{ContaId}:{Numero}";
}
