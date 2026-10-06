using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pix.Processing.Domain.ValueObjects;

public readonly record struct Fingerprint
{
    public string Versao { get; }
    public string Algoritmo { get; }
    public string Hash { get; }

    public Fingerprint(string versao, string hash, string algoritmo = "SHA256")
    {
        if (string.IsNullOrWhiteSpace(versao))
            throw new ArgumentException("Versão não pode ser vazia.", nameof(versao));

        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Hash não pode ser vazio.", nameof(hash));

        Versao = versao;
        Hash = hash;
        Algoritmo = algoritmo;
    }

    public static Fingerprint GerarDePayload(string versao, IDictionary<string, object?> campos)
    {
        // Ordena chaves de forma determinística
        var sortedKeys = campos.Keys.OrderBy(k => k, StringComparer.Ordinal);
        var sb = new StringBuilder();

        foreach (var key in sortedKeys)
        {
            var val = campos[key];
            string formattedVal;
            if (val is decimal d)
            {
                formattedVal = d.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else if (val is double db)
            {
                formattedVal = db.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else if (val is DateTime dt)
            {
                formattedVal = dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
            else if (val is DateTimeOffset dto)
            {
                formattedVal = dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
            else
            {
                formattedVal = val?.ToString() ?? "";
            }

            sb.Append(key).Append('=').Append(formattedVal).Append(';');
        }

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hashBytes = sha.ComputeHash(bytes);
        var hex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return new Fingerprint(versao, hex, "SHA256");
    }

    public override string ToString() => $"{Algoritmo}:{Versao}:{Hash}";
}
