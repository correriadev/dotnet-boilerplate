using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Pix.Processing.Domain.Telemetry;

public static class SanitizadorTelemetriaPolicy
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "cpf", "cnpj", "email", "chave_pix", "chavepix", "chave", "pix_key", "pixkey",
        "conta_corrente", "contacorrente", "conta", "account", "agencia", "agency",
        "password", "senha", "token", "secret", "telefone", "phone", "celular", "mobile", "nome", "name"
    };

    private static readonly Regex CpfRegex = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b|\b\d{11}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(\+?55\s?)?(\(?\d{2}\)?\s?)?(9\d{4}[-\s]?\d{4}|\d{4}[-\s]?\d{4})|\+?55\d{10,11}", RegexOptions.Compiled);
    private static readonly Regex ContaRegex = new(@"\b(agencia|conta|agency|account)[\s:]*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContemDadosSensiveis(string tagKey)
    {
        if (string.IsNullOrWhiteSpace(tagKey)) return false;
        return SensitiveKeys.Contains(tagKey) || SensitiveKeys.Any(s => tagKey.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ValorContemPii(string tagValue)
    {
        if (string.IsNullOrWhiteSpace(tagValue)) return false;
        return CpfRegex.IsMatch(tagValue) || EmailRegex.IsMatch(tagValue) || PhoneRegex.IsMatch(tagValue) || ContaRegex.IsMatch(tagValue);
    }

    public static bool ObjetoContemPii(object? value)
    {
        if (value == null) return false;

        if (value is string str)
            return ValorContemPii(str);

        if (value is long or int or ulong or uint)
        {
            var numStr = value.ToString();
            if (numStr != null && (numStr.Length == 11 || numStr.Length == 14)) // Possível CPF/CNPJ ou telefone
                return true;
        }

        return false;
    }

    public static void AplicarTagsSeguras(Activity activity, IdentificadorTecnico ids)
    {
        // Limpa tanto Tags string quanto TagObjects (boxed/numéricas)
        var tagsParaRemover = activity.TagObjects
            .Where(t => ContemDadosSensiveis(t.Key) || ObjetoContemPii(t.Value))
            .Select(t => t.Key)
            .Distinct()
            .ToList();

        foreach (var key in tagsParaRemover)
        {
            activity.SetTag(key, null);
        }

        activity.SetTag("pedido_id", ids.PedidoId.ToString());
        activity.SetTag("end_to_end_id", ids.EndToEndId);
    }

    public static IDictionary<string, object?> SanitizarTags(IDictionary<string, object?> tags)
    {
        var sanitized = new Dictionary<string, object?>();
        foreach (var kvp in tags)
        {
            if (ContemDadosSensiveis(kvp.Key)) continue;
            if (ObjetoContemPii(kvp.Value)) continue;
            sanitized[kvp.Key] = kvp.Value;
        }
        return sanitized;
    }
}
