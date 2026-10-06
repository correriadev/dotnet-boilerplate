namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct OwnershipVersion
{
    public string Owner { get; }
    public long Generation { get; }
    public int Version { get; }
    public DateTimeOffset ExpiresAt { get; }

    public OwnershipVersion(string owner, long generation, int version, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner não pode ser vazio.", nameof(owner));

        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "Generation não pode ser negativa.");

        if (version < 0)
            throw new ArgumentOutOfRangeException(nameof(version), "Version não pode ser negativa.");

        Owner = owner.Trim();
        Generation = generation;
        Version = version;
        ExpiresAt = expiresAt;
    }

    public bool EstaExpirado(DateTimeOffset agora) => agora >= ExpiresAt;

    public OwnershipVersion IncrementarVersao(DateTimeOffset novoExpiresAt) =>
        new(Owner, Generation, Version + 1, novoExpiresAt);

    public OwnershipVersion ProximoOwner(string novoOwner, DateTimeOffset novoExpiresAt) =>
        new(novoOwner, Generation + 1, 0, novoExpiresAt);

    public override string ToString() => $"[Owner={Owner}, Gen={Generation}, Ver={Version}, Exp={ExpiresAt:O}]";
}
