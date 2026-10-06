using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Features.ExecutePayment;

public record ExecutePaymentCommand(
    Guid PedidoId,
    ContaId ContaId,
    long Sequencia,
    EndToEndId EndToEndId,
    Money Valor,
    string Owner,
    long Generation,
    DateTimeOffset ExpiresAt);

public enum ExecutionResultStatus
{
    Confirmado = 1,
    RejeitadoDefinitivo = 2,
    ResultadoIncerto = 3,
    ConflitoAquisicao = 4,
    JaConcluido = 5,
    FalhaConsolidacao = 6
}

public record ExecutionResult(
    ExecutionResultStatus Status,
    ExternalEvidence? Evidencia = null,
    string? Mensagem = null);

public class ExecutePaymentUseCase
{
    private readonly IExecutionStore _executionStore;
    private readonly IOutbankAcl _outbankAcl;

    public ExecutePaymentUseCase(IExecutionStore executionStore, IOutbankAcl outbankAcl)
    {
        _executionStore = executionStore;
        _outbankAcl = outbankAcl;
    }

    public async Task<ExecutionResult> ExecutarAsync(ExecutePaymentCommand comando, CancellationToken ct = default)
    {
        var agora = DateTimeOffset.UtcNow;

        // 1. Tentar adquirir execução no ExecutionStore (INV-05, P-05)
        var aquisicao = await _executionStore.AdquirirExecucaoAsync(
            comando.EndToEndId,
            comando.PedidoId,
            comando.ContaId,
            comando.Sequencia,
            comando.Owner,
            comando.Generation,
            comando.ExpiresAt,
            ct);

        if (aquisicao.Status == StatusAquisicao.JaConcluido)
        {
            return new ExecutionResult(ExecutionResultStatus.JaConcluido, null, "Operação já concluída previamente.");
        }

        if (aquisicao.Status == StatusAquisicao.Conflito)
        {
            if (aquisicao.EstadoAtual == EstadoPedidoPix.ResultadoIncerto || aquisicao.EstadoAtual == EstadoPedidoPix.EmTriagem)
            {
                return new ExecutionResult(ExecutionResultStatus.ResultadoIncerto, null, aquisicao.Mensagem ?? "Operação já possui intenção registrada ou estado incerto/triagem sem prova externa.");
            }
            return new ExecutionResult(ExecutionResultStatus.ConflitoAquisicao, null, aquisicao.Mensagem ?? "Conflito: Operação já está sendo processada por outro executor.");
        }

        // 2. Registrar intenção de envio antes da chamada externa (INV-06)
        await _executionStore.RegistrarIntencaoAsync(comando.EndToEndId, comando.Owner, 1, ct);

        // 3. Execução externa Outbank fora da transação de banco de dados (D-11, INV-06)
        ExternalEvidence evidencia;
        try
        {
            evidencia = await _outbankAcl.ExecutarEnvioPixAsync(comando.EndToEndId, comando.Valor, comando.ContaId, ct);
        }
        catch (TimeoutException tex)
        {
            // Timeout pós-envio sem prova de resultado produz incerteza conservadora (INV-06, EV-05, EV-06)
            var evidenciaIncerta = new ExternalEvidence("EnvioPix", "Timeout", "Outbank", tex.Message, DateTimeOffset.UtcNow, false);
            var eventoIncerto = new ResultadoFinanceiroIncertoEvento(
                Guid.NewGuid().ToString(),
                comando.PedidoId.ToString(),
                comando.PedidoId,
                comando.ContaId,
                comando.EndToEndId,
                "Timeout pós-envio Outbank",
                tex.Message,
                DateTimeOffset.UtcNow);

            var salvo = await _executionStore.ConcluirExecucaoAsync(
                comando.EndToEndId,
                comando.PedidoId,
                evidenciaIncerta,
                EstadoPedidoPix.ResultadoIncerto,
                comando.Owner,
                2,
                eventoIncerto,
                ct);

            if (!salvo)
            {
                return new ExecutionResult(ExecutionResultStatus.FalhaConsolidacao, evidenciaIncerta, "Falha de commit ao consolidar resultado incerto (TX-03).");
            }

            return new ExecutionResult(ExecutionResultStatus.ResultadoIncerto, evidenciaIncerta, "Timeout pós-envio Outbank.");
        }
        catch (Exception ex)
        {
            var evidenciaFalha = new ExternalEvidence("EnvioPix", "Erro", "Outbank", ex.Message, DateTimeOffset.UtcNow, false);
            var eventoIncerto = new ResultadoFinanceiroIncertoEvento(
                Guid.NewGuid().ToString(),
                comando.PedidoId.ToString(),
                comando.PedidoId,
                comando.ContaId,
                comando.EndToEndId,
                "Falha na comunicação Outbank",
                ex.Message,
                DateTimeOffset.UtcNow);

            var salvo = await _executionStore.ConcluirExecucaoAsync(
                comando.EndToEndId,
                comando.PedidoId,
                evidenciaFalha,
                EstadoPedidoPix.ResultadoIncerto,
                comando.Owner,
                2,
                eventoIncerto,
                ct);

            if (!salvo)
            {
                return new ExecutionResult(ExecutionResultStatus.FalhaConsolidacao, evidenciaFalha, "Falha de commit ao consolidar resultado incerto (TX-03).");
            }

            return new ExecutionResult(ExecutionResultStatus.ResultadoIncerto, evidenciaFalha, ex.Message);
        }

        // 4. Consolidar resultado em TX-03 (INV-07, INV-12)
        var estadoFinal = evidencia.Sucesso ? EstadoPedidoPix.Confirmado : EstadoPedidoPix.RejeitadoDefinitivo;
        var eventoConclusao = new OperacaoConcluidaEvento(
            Guid.NewGuid().ToString(),
            comando.PedidoId.ToString(),
            comando.PedidoId,
            comando.EndToEndId,
            estadoFinal,
            evidencia.Sucesso ? "CONFIRMADO" : "REJEITADO",
            comando.ContaId,
            DateTimeOffset.UtcNow);

        var concluido = await _executionStore.ConcluirExecucaoAsync(
            comando.EndToEndId,
            comando.PedidoId,
            evidencia,
            estadoFinal,
            comando.Owner,
            2,
            eventoConclusao,
            ct);

        if (!concluido)
        {
            return new ExecutionResult(ExecutionResultStatus.FalhaConsolidacao, evidencia, "Falha de commit na consolidação do resultado (versão ou lease expirado).");
        }

        return new ExecutionResult(
            estadoFinal == EstadoPedidoPix.Confirmado ? ExecutionResultStatus.Confirmado : ExecutionResultStatus.RejeitadoDefinitivo,
            evidencia);
    }
}
