using Microsoft.AspNetCore.Mvc;
using Pix.Processing.Application.Features.AcceptRequest;
using Pix.Processing.Application.Models;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure.Auth;

namespace Pix.CashOut.Api.Controllers;

public record CashOutRequestDto(
    string ClienteId,
    string ContaId,
    string IdempotencyKey,
    decimal Valor,
    string Moeda,
    string EndToEndId);

[ApiController]
[Route("api/pix/cash-out")]
public class CashOutController : ControllerBase
{
    private readonly AcceptRequestUseCase _acceptRequestUseCase;
    private readonly IOidcAuthValidator _authValidator;

    public CashOutController(AcceptRequestUseCase acceptRequestUseCase, IOidcAuthValidator authValidator)
    {
        _acceptRequestUseCase = acceptRequestUseCase;
        _authValidator = authValidator;
    }

    [HttpPost]
    public async Task<IActionResult> SolicitarCashOut([FromBody] CashOutRequestDto request, CancellationToken ct)
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
            ["endToEndId"] = request.EndToEndId
        };

        var comando = new AcceptRequestCommand(
            new ClienteId(request.ClienteId),
            new ContaId(request.ContaId),
            "CashOut",
            request.IdempotencyKey,
            new Money(request.Valor, request.Moeda),
            payloadDict,
            request.EndToEndId);

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
