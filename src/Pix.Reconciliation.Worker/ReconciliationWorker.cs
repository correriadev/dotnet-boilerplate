using Microsoft.EntityFrameworkCore;
using Pix.Processing.Application.Features.ExecutePayment;
using Pix.Processing.Application.Features.ReconcilePayment;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;

namespace Pix.Reconciliation.Worker;

public class ReconciliationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReconciliationWorker> _logger;

    public ReconciliationWorker(IServiceProvider serviceProvider, ILogger<ReconciliationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task ProcessarReconciliacaoAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PixDbContext>();
        var useCase = scope.ServiceProvider.GetRequiredService<ReconcilePaymentUseCase>();
        var agora = DateTimeOffset.UtcNow;

        // PIX-AUD-02: Recuperação de pedidos EmProcessamento com intenção registrada e lease expirado (INV-06/09; TS-055/056)
        var pedidosTravados = await db.Pedidos
            .Where(p => p.Estado == EstadoPedidoPix.EmProcessamento &&
                        p.IntencaoEnvioRegistrada &&
                        (p.LeaseExpiresAt == null || p.LeaseExpiresAt <= agora) &&
                        p.EndToEndId != null)
            .OrderBy(p => p.AtualizadoEm)
            .Take(10)
            .ToListAsync(ct);

        foreach (var travado in pedidosTravados)
        {
            _logger.LogWarning("Recuperando pedido {PedidoId} travado em EmProcessamento com intenção registrada e lease expirado. Movendo para ResultadoIncerto e bloqueando conta.", travado.Id);
            travado.MarcarResultadoIncerto(
                "Crash pós-intenção com lease expirado detectado pelo reconciliador",
                new ExternalEvidence("Reconciliacao", "CrashPostIntention", "ReconciliationWorker", "CRASH_RECON", agora, false),
                agora);

            if (travado.SequenciaConta.HasValue)
            {
                var coordTravada = await db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == travado.ContaId, ct);
                coordTravada?.Bloquear(travado.SequenciaConta.Value, "Incerteza por crash pós-intenção", agora);
            }

            var regTravado = await db.ProcessedIds.FirstOrDefaultAsync(r => r.EndToEndId == travado.EndToEndId, ct);
            if (regTravado != null)
            {
                regTravado.Concluir(new ExternalEvidence("Reconciliacao", "CrashPostIntention", "ReconciliationWorker", "CRASH_RECON", agora, false), EstadoPedidoPix.ResultadoIncerto, agora);
            }
        }

        if (pedidosTravados.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // 1. Reconciliação autoritativa de pedidos incertos e em triagem (INV-07, INV-08)
        var pedidosIncertos = await db.Pedidos
            .Where(p => (p.Estado == EstadoPedidoPix.ResultadoIncerto || p.Estado == EstadoPedidoPix.EmTriagem) && p.EndToEndId != null)
            .OrderBy(p => p.AtualizadoEm)
            .Take(10)
            .ToListAsync(ct);

        foreach (var pedido in pedidosIncertos)
        {
            _logger.LogInformation("Reconciliando pedido incerto/triagem {PedidoId}...", pedido.Id);
            var novoOwner = "ReconciliationWorker-01";
            var novoExpiresAt = agora.AddMinutes(2);

            // Reconciliador assume formalmente lease e generation via CAS antes de concluir (PIX-REV-02)
            if (pedido.OwnerVigente != novoOwner || (pedido.LeaseExpiresAt.HasValue && agora >= pedido.LeaseExpiresAt.Value))
            {
                pedido.AssumirLeaseExpirado(novoOwner, novoExpiresAt, agora);
                var registro = await db.ProcessedIds.FirstOrDefaultAsync(r => r.EndToEndId == pedido.EndToEndId, ct);
                if (registro != null)
                {
                    registro.RenovarLease(novoOwner, registro.Generation + 1, novoExpiresAt, agora);
                }
                await db.SaveChangesAsync(ct);
            }

            var reconciliou = await useCase.ReconciliarAsync(
                pedido.EndToEndId!.Value,
                pedido.Id,
                pedido.ContaId,
                novoOwner,
                pedido.Versao,
                ct);

            if (!reconciliou && pedido.Estado == EstadoPedidoPix.ResultadoIncerto)
            {
                // Evidência não conclusiva: encaminha formalmente à Triagem para investigação auditada (INV-07, TS-014)
                pedido.EncaminharParaTriagem("Evidência externa ausente ou inconclusiva durante reconciliação.", DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(ct);
                _logger.LogWarning("Pedido {PedidoId} encaminhado para EmTriagem (evidência insuficiente).", pedido.Id);
            }
        }

        // 2. Triagem e recuperação de comandos bloqueados por lacuna/DLQ (INV-09, INV-14)
        var comandosPendentes = await db.PendingCommands
            .Where(c => c.Status == "BLOCKED" || c.Status == "PENDING")
            .OrderBy(c => c.ContaId)
            .ThenBy(c => c.Sequencia)
            .Take(10)
            .ToListAsync(ct);

        foreach (var cmd in comandosPendentes)
        {
            var targetContaId = new ContaId(cmd.ContaId);
            var coord = await db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == targetContaId, ct);
            if (coord != null && coord.Estado == EstadoCoordenacao.Livre && coord.ProximaSequenciaExecutavel == cmd.Sequencia)
            {
                // Lacuna sanada: desbloqueia comando para execução pelo scheduler (INV-09)
                cmd.Status = "READY";
                _logger.LogInformation("Comando pendente {EventId} para conta {ContaId} seq {Seq} pronto para execução.", cmd.EventId, cmd.ContaId, cmd.Sequencia);
            }
        }

        await db.SaveChangesAsync(ct);

        // PIX-AUD-03: Executa pendências marcadas como READY de forma autônoma sem depender de reentrega do broker (INV-09; TS-051)
        var comandosProntos = await db.PendingCommands
            .Where(c => c.Status == "READY")
            .OrderBy(c => c.ContaId)
            .ThenBy(c => c.Sequencia)
            .Take(10)
            .ToListAsync(ct);

        if (comandosProntos.Count > 0)
        {
            var executeUseCase = scope.ServiceProvider.GetRequiredService<ExecutePaymentUseCase>();

            foreach (var cmd in comandosProntos)
            {
                var targetContaId = new ContaId(cmd.ContaId);
                var coord = await db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == targetContaId, ct);
                if (coord == null || coord.Estado == EstadoCoordenacao.Bloqueada || coord.ProximaSequenciaExecutavel != cmd.Sequencia)
                {
                    continue;
                }

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(cmd.CorpoJson);
                    var root = doc.RootElement;
                    var pedidoId = root.GetProperty("pedidoId").GetGuid();
                    var endToEndIdStr = root.GetProperty("endToEndId").GetString()!;
                    var endToEndId = new EndToEndId(endToEndIdStr);
                    var contaId = new ContaId(cmd.ContaId);
                    var sequencia = cmd.Sequencia;
                    var quantia = root.GetProperty("valor").GetDecimal();
                    var moeda = root.TryGetProperty("moeda", out var moedaProp) ? moedaProp.GetString()! : "BRL";
                    var valor = new Money(quantia, moeda);

                    var comando = new ExecutePaymentCommand(
                        pedidoId,
                        contaId,
                        sequencia,
                        endToEndId,
                        valor,
                        $"ReconciliationScheduler-{cmd.ContaId}",
                        1,
                        DateTimeOffset.UtcNow.AddMinutes(2));

                    var resultado = await executeUseCase.ExecutarAsync(comando, ct);
                    if (resultado.Status == ExecutionResultStatus.Confirmado ||
                        resultado.Status == ExecutionResultStatus.RejeitadoDefinitivo ||
                        resultado.Status == ExecutionResultStatus.JaConcluido)
                    {
                        cmd.Status = "COMPLETED";
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation("Comando pendente READY {EventId} executado com sucesso: {Status}", cmd.EventId, resultado.Status);
                    }
                    else if (resultado.Status == ExecutionResultStatus.ResultadoIncerto)
                    {
                        cmd.Status = "UNCERTAIN";
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha na execução autônoma do comando READY {EventId}", cmd.EventId);
                }
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reconciliation Worker iniciado com suporte a consulta autoritativa e triagem DLQ.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarReconciliacaoAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no ciclo do ReconciliationWorker.");
            }

            await Task.Delay(3000, stoppingToken);
        }
    }
}
