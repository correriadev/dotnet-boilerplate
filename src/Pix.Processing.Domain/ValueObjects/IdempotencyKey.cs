namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct IdempotencyKey
{
    public ClienteId ClienteId { get; }
    public string TipoOperacao { get; }
    public string Chave { get; }

    public IdempotencyKey(ClienteId clienteId, string tipoOperacao, string chave)
    {
        if (string.IsNullOrWhiteSpace(tipoOperacao))
            throw new ArgumentException("Tipo da operação não pode ser vazio.", nameof(tipoOperacao));

        if (string.IsNullOrWhiteSpace(chave))
            throw new ArgumentException("Chave de idempotência não pode ser vazia.", nameof(chave));

        if (chave.Length > 128)
            throw new ArgumentException("Chave de idempotência não pode exceder 128 caracteres.", nameof(chave));

        ClienteId = clienteId;
        TipoOperacao = tipoOperacao.Trim().ToLowerInvariant();
        Chave = chave.Trim();
    }

    public IdempotencyKey(string cliente, string op, string chave)
        : this(new ClienteId(cliente), op, chave)
    {
    }

    public string ChaveComposta => $"{ClienteId.Valor}:{TipoOperacao}:{Chave}";

    public override string ToString() => ChaveComposta;
}
