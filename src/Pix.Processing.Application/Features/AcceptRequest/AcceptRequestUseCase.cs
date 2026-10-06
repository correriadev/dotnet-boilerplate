using System.Text.Json;
using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Features.AcceptRequest;

public record AcceptRequestCommand(
    ClienteId ClienteId,
    ContaId ContaId,
    string TipoOperacao,
    string IdempotencyKeyVal,
    Money Valor,
    IDictionary<string, object?> PayloadCampos,
    string? EndToEndIdVal = null);

public class AcceptRequestUseCase
{
    private readonly IAutorizacaoPort _autorizacaoPort;
    private readonly IAcceptanceStore _acceptanceStore;
    private readonly IAdmissionLimiter _admissionLimiter;

    public AcceptRequestUseCase(
        IAutorizacaoPort autorizacaoPort,
        IAcceptanceStore acceptanceStore,
        IAdmissionLimiter admissionLimiter)
    {
        _autorizacaoPort = autorizacaoPort;
        _acceptanceStore = acceptanceStore;
        _admissionLimiter = admissionLimiter;
    }

    public async Task<AceiteResultado> ExecutarAsync(AcceptRequestCommand comando, CancellationToken ct = default)
    {
        // 1. Autorizar solicitação (UC-01, INV-01)
        var autorizado = await _autorizacaoPort.AutorizarAsync(comando.ClienteId, comando.ContaId, comando.TipoOperacao, ct);
        if (!autorizado)
        {
            return new AceiteResultado(StatusResultadoAceite.ConflitoDeConteudo, null, "Acesso não autorizado para o cliente/conta.");
        }

        // 2. Construir chave e fingerprint (UC-02, INV-02)
        var idempotencyKey = new IdempotencyKey(comando.ClienteId, comando.TipoOperacao, comando.IdempotencyKeyVal);
        var fingerprint = Fingerprint.GerarDePayload("v1", comando.PayloadCampos);

        // 3. Recuperar aceite durável existente antes da admissão (UC-03, D-14)
        var aceiteExistente = await _acceptanceStore.ObterAceitePorChaveAsync(idempotencyKey, ct);
        if (aceiteExistente != null)
        {
            if (aceiteExistente.Fingerprint == fingerprint)
            {
                return new AceiteResultado(StatusResultadoAceite.Replay, aceiteExistente);
            }
            else
            {
                return new AceiteResultado(StatusResultadoAceite.ConflitoDeConteudo, null, "Conflito: Chave de idempotência reenviada com fingerprint diferente.");
            }
        }

        // 4. Rate limiting / Admissão (UC-04, INV-18)
        var admissao = await _admissionLimiter.VerificarAdmissaoAsync(comando.ClienteId, comando.TipoOperacao, ct);
        if (admissao.Status != StatusAdmissao.Admitido)
        {
            // Releitura do banco para checar se houve race winner concorrente (D-14, TS-093)
            for (var tentativa = 0; tentativa < 3; tentativa++)
            {
                var aceiteRace = await _acceptanceStore.ObterAceitePorChaveAsync(idempotencyKey, ct);
                if (aceiteRace != null)
                {
                    if (aceiteRace.Fingerprint == fingerprint)
                        return new AceiteResultado(StatusResultadoAceite.Replay, aceiteRace);
                    else
                        return new AceiteResultado(StatusResultadoAceite.ConflitoDeConteudo, null, "Conflito de fingerprint concorrente.");
                }

                if (admissao.Status == StatusAdmissao.Indisponivel || tentativa == 2)
                    break;

                await Task.Delay(20, ct);
            }

            return new AceiteResultado(StatusResultadoAceite.ConflitoDeConteudo, null, $"Solicitação negada na admissão: {admissao.Status} - {admissao.Mensagem}");
        }

        // 5. Aceite Atômico TX-01 (UC-05 / UC-06)
        var ehFinanceiro = comando.TipoOperacao.Equals("CashOut", StringComparison.OrdinalIgnoreCase);
        var tipoOperacaoEnum = ehFinanceiro ? TipoOperacaoPix.CashOut : TipoOperacaoPix.Cobranca;
        var endToEndId = string.IsNullOrWhiteSpace(comando.EndToEndIdVal) ? (EndToEndId?)null : new EndToEndId(comando.EndToEndIdVal);

        var pedido = new PedidoPix(
            Guid.NewGuid(),
            comando.ClienteId,
            comando.ContaId,
            tipoOperacaoEnum,
            endToEndId,
            comando.Valor);

        Func<Guid, AceiteOriginal> factoryAceite = (pedidoId) =>
        {
            var corpoJson = JsonSerializer.Serialize(new
            {
                pedidoId,
                status = "Aceito",
                endToEndId = endToEndId?.Valor,
                valor = comando.Valor.Quantia,
                moeda = comando.Valor.Moeda
            });

            return new AceiteOriginal(
                pedidoId,
                202,
                "{}",
                corpoJson,
                fingerprint,
                DateTimeOffset.UtcNow);
        };

        return await _acceptanceStore.ConfirmarAceiteAsync(pedido, idempotencyKey, fingerprint, factoryAceite, ehFinanceiro, ct);
    }
}
