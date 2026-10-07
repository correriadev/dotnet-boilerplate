using System.Text.RegularExpressions;

namespace Pix.Processing.Domain.Telemetry;

public readonly record struct IdentificadorTecnico
{
    // Padrão BACEN Oficial: Prefixo 'E' (envio) ou 'M' (devolução) + 8 dígitos ISPB + 12 dígitos Data/Hora (yyyyMMddHHmm) + 11 alfanuméricos = 32 chars
    private static readonly Regex EndToEndBacenRegex = new(@"^[EM]\d{20}[a-zA-Z0-9]{11}$", RegexOptions.Compiled);
    private static readonly Regex PiiPattern = new(@"(\d{3}\.\d{3}\.\d{3}-\d{2}|\b\d{11}\b|@)", RegexOptions.Compiled);

    public Guid PedidoId { get; }
    public string EndToEndId { get; }

    private IdentificadorTecnico(Guid pedidoId, string endToEndId)
    {
        PedidoId = pedidoId;
        EndToEndId = endToEndId;
    }

    public static IdentificadorTecnico Validar(Guid pedidoId, string endToEndId)
    {
        if (pedidoId == Guid.Empty)
            throw new ArgumentException("PedidoId não pode ser Guid.Empty.", nameof(pedidoId));

        if (string.IsNullOrWhiteSpace(endToEndId))
            throw new ArgumentException("EndToEndId não pode ser vazio.", nameof(endToEndId));

        if (PiiPattern.IsMatch(endToEndId))
            throw new ArgumentException("EndToEndId contém padrão de informação pessoal identificável (CPF/e-mail).", nameof(endToEndId));

        if (endToEndId.Length != 32 || !EndToEndBacenRegex.IsMatch(endToEndId))
            throw new ArgumentException("EndToEndId BACEN inválido. Deve possuir exatamente 32 caracteres iniciando com 'E' ou 'M' seguido de ISPB (8d), data/hora (12d) e sufixo alfanumérico (11c).", nameof(endToEndId));

        return new IdentificadorTecnico(pedidoId, endToEndId);
    }
}
