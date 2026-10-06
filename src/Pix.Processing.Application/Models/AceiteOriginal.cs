using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Models;

public record AceiteOriginal(
    Guid PedidoId,
    int StatusCode,
    string HeadersJson,
    string CorpoJson,
    Fingerprint Fingerprint,
    DateTimeOffset CriadoEm);

public enum StatusResultadoAceite
{
    Criado = 1,
    Replay = 2,
    ConflitoDeConteudo = 3,
    ResultadoDeCommitIncerto = 4
}

public record AceiteResultado(
    StatusResultadoAceite Status,
    AceiteOriginal? Aceite,
    string? MensagemErro = null);
