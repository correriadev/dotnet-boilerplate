using Microsoft.AspNetCore.Mvc;
using Pix.Processing.Application.Features.AcceptRequest;
using Pix.Processing.Application.Models;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure.Auth;

namespace Pix.Payment.Api.Controllers;

public record CriarCobrancaRequestDto(
    string ClienteId,
    string ContaId,
    string IdempotencyKey,
    decimal Valor,
    string Moeda,
    string CobrancaRef);

[ApiController]
[Route("api/pix/cobranca")]
public class BillingController : ControllerBase
{
    private readonly AcceptRequestUseCase _acceptRequestUseCase;
    private readonly IOidcAuthValidator _authValidator;

    public BillingController(AcceptRequestUseCase acceptRequestUseCase, IOidcAuthValidator authValidator)
    {
        _acceptRequestUseCase = acceptRequestUseCase;
        _authValidator = authValidator;
    }

    [HttpPost]
    public async Task<IActionResult> SolicitarCobranca([FromBody] CriarCobrancaRequestDto request, CancellationToken ct)
    {
        var authHeader = Request?.Headers?.Authorization.FirstOrDefault();
        var authResult = await _authValidator.ValidarTokenAsync(authHeader, ct);
        if (!authResult.Autenticado)
        {
            return Unauthorized(new { erro = authResult.Mensagem ?? "Não autorizado." });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.ClienteId) || string.IsNullOrWhiteSpace(request.ContaId))
            return BadRequest("Parâmetros obrigatórios ausentes.");

        var payloadDict = new Dictionary<string, object?>
        {
            ["clienteId"] = request.ClienteId,
            ["contaId"] = request.ContaId,
            ["valor"] = request.Valor,
            ["moeda"] = request.Moeda,
            ["cobrancaRef"] = request.CobrancaRef
        };

        var comando = new AcceptRequestCommand(
            new ClienteId(request.ClienteId),
            new ContaId(request.ContaId),
            "Cobranca",
            request.IdempotencyKey,
            new Money(request.Valor, request.Moeda),
            payloadDict);

        var resultado = await _acceptRequestUseCase.ExecutarAsync(comando, ct);

        return resultado.Status switch
        {
            StatusResultadoAceite.Criado => StatusCode(202, resultado.Aceite),
            StatusResultadoAceite.Replay => StatusCode(200, resultado.Aceite),
            StatusResultadoAceite.ConflitoDeConteudo => StatusCode(409, new { erro = resultado.MensagemErro }),
            StatusResultadoAceite.ResultadoDeCommitIncerto => StatusCode(500, new { erro = resultado.MensagemErro }),
            _ => StatusCode(500, new { erro = "Status inesperado." })
        };
    }
}
