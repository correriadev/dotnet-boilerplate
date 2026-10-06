using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure.Entities;

namespace Pix.Processing.Infrastructure.Adapters;

public class ExecutionStore : IExecutionStore
{
    private readonly PixDbContext _db;

    public ExecutionStore(PixDbContext db)
    {
        _db = db;
    }

    public async Task<AquisicaoResultado> AdquirirExecucaoAsync(
        EndToEndId endToEndId,
        Guid pedidoId,
        ContaId contaId,
        long sequencia,
        string owner,
        long generation,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        var existente = await _db.ProcessedIds
            .FirstOrDefaultAsync(r => r.EndToEndId == endToEndId, ct);
        var pedido = await _db.Pedidos
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

        var agora = DateTimeOffset.UtcNow;

        // PIX-AUD-01: Checa estado terminal PRIMEIRO, antes de verificar intenção ou bloqueio de coordenação
        var terminalState = existente?.Estado switch
        {
            EstadoPedidoPix.Confirmado => EstadoPedidoPix.Confirmado,
            EstadoPedidoPix.RejeitadoDefinitivo => EstadoPedidoPix.RejeitadoDefinitivo,
            _ => pedido?.Estado switch
            {
                EstadoPedidoPix.Confirmado => EstadoPedidoPix.Confirmado,
                EstadoPedidoPix.RejeitadoDefinitivo => EstadoPedidoPix.RejeitadoDefinitivo,
                _ => (EstadoPedidoPix?)null
            }
        };

        if (terminalState.HasValue)
        {
            return new AquisicaoResultado(StatusAquisicao.JaConcluido, terminalState.Value, "Operação já concluída previamente.");
        }

        // PIX-REV-01 & PIX-AUD-02: Recusa nova tentativa se intenção já foi registrada ou estado é ResultadoIncerto / EmTriagem
        if ((existente != null && (existente.IntencaoRegistrada || existente.Estado == EstadoPedidoPix.ResultadoIncerto || existente.Estado == EstadoPedidoPix.EmTriagem)) ||
            (pedido != null && (pedido.IntencaoEnvioRegistrada || pedido.Estado == EstadoPedidoPix.ResultadoIncerto || pedido.Estado == EstadoPedidoPix.EmTriagem)))
        {
            // PIX-AUD-02: Se estava EmProcessamento e o lease já expirou, transiciona conservadoramente para ResultadoIncerto e bloqueia coordenação
            if (pedido != null && pedido.Estado == EstadoPedidoPix.EmProcessamento && (pedido.LeaseExpiresAt == null || pedido.LeaseExpiresAt <= agora))
            {
                pedido.MarcarResultadoIncerto(
                    "Tentativa de aquisição pós-crash com intenção registrada e lease expirado",
                    new ExternalEvidence("CashOut", "PostIntentionLeaseExpired", "Sistema", "LEASE_EXPIRED", agora, false),
                    agora);

                if (pedido.SequenciaConta.HasValue)
                {
                    var coordConta = await _db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == pedido.ContaId, ct);
                    coordConta?.Bloquear(pedido.SequenciaConta.Value, "Incerteza por expiração de lease", agora);
                }

                if (existente != null)
                {
                    existente.Concluir(new ExternalEvidence("CashOut", "PostIntentionLeaseExpired", "Sistema", "LEASE_EXPIRED", agora, false), EstadoPedidoPix.ResultadoIncerto, agora);
                }

                await _db.SaveChangesAsync(ct);
            }

            var estadoAtual = pedido?.Estado ?? existente?.Estado ?? EstadoPedidoPix.ResultadoIncerto;
            return new AquisicaoResultado(StatusAquisicao.Conflito, estadoAtual, "Operação possui intenção de envio registrada ou estado incerto/triagem. Reenvio recusado sem prova externa autoritativa.");
        }

        // PIX-REV-03: Valida coordenação da conta (bloqueio e sequência) antes do despacho externo
        var coord = await _db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == contaId, ct);
        if (coord != null)
        {
            if (coord.Estado == EstadoCoordenacao.Bloqueada)
            {
                return new AquisicaoResultado(StatusAquisicao.Conflito, existente?.Estado ?? pedido?.Estado, "Conta está bloqueada na coordenação.");
            }
            if (sequencia > coord.ProximaSequenciaExecutavel)
            {
                return new AquisicaoResultado(StatusAquisicao.Conflito, existente?.Estado ?? pedido?.Estado, $"Lacuna de sequência: sequência {sequencia} > próxima executável {coord.ProximaSequenciaExecutavel}.");
            }
        }

        if (existente != null)
        {
            if (existente.ExpiresAt > agora && existente.Owner != owner)
            {
                return new AquisicaoResultado(StatusAquisicao.Conflito, existente.Estado, "Operação bloqueada por outro executor com lease ativo.");
            }

            // Lease expirado: assume posse (CAS)
            existente.RenovarLease(owner, generation, expiresAt, agora);
            if (pedido != null)
            {
                if (pedido.Estado == EstadoPedidoPix.Aceito || pedido.Estado == EstadoPedidoPix.EmTriagem || pedido.Estado == EstadoPedidoPix.ResultadoIncerto)
                {
                    pedido.IniciarProcessamento(owner, agora, expiresAt, generation);
                }
                else if (pedido.Estado == EstadoPedidoPix.EmProcessamento && (pedido.LeaseExpiresAt == null || pedido.LeaseExpiresAt <= agora || pedido.OwnerVigente == owner))
                {
                    pedido.AssumirLeaseExpirado(owner, expiresAt, agora, generation);
                }
            }
            await _db.SaveChangesAsync(ct);
            return new AquisicaoResultado(StatusAquisicao.Sucesso, existente.Estado);
        }

        var novoRegistro = new RegistroExecucao(
            endToEndId,
            pedidoId,
            contaId,
            sequencia,
            owner,
            generation,
            expiresAt,
            agora);

        try
        {
            _db.ProcessedIds.Add(novoRegistro);
            if (pedido != null && (pedido.Estado == EstadoPedidoPix.Aceito || pedido.Estado == EstadoPedidoPix.EmTriagem || pedido.Estado == EstadoPedidoPix.ResultadoIncerto))
            {
                pedido.IniciarProcessamento(owner, agora, expiresAt, generation);
            }
            await _db.SaveChangesAsync(ct);
            return new AquisicaoResultado(StatusAquisicao.Sucesso);
        }
        catch (DbUpdateException)
        {
            // Race condition: outro processo inseriu concorrentemente
            var recordConcorrente = await _db.ProcessedIds
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EndToEndId == endToEndId, ct);

            return new AquisicaoResultado(StatusAquisicao.Conflito, recordConcorrente?.Estado, "Conflito concorrente na aquisição de EndToEndId.");
        }
    }

    public async Task RegistrarIntencaoAsync(
        EndToEndId endToEndId,
        string owner,
        int versaoEsperada,
        CancellationToken ct = default)
    {
        var registro = await _db.ProcessedIds.FirstOrDefaultAsync(r => r.EndToEndId == endToEndId, ct);
        if (registro != null)
        {
            registro.RegistrarIntencao(DateTimeOffset.UtcNow);
        }
        var pedido = await _db.Pedidos.FirstOrDefaultAsync(p => p.EndToEndId == endToEndId, ct);
        if (pedido != null && pedido.Estado == EstadoPedidoPix.EmProcessamento)
        {
            pedido.RegistrarIntencaoEnvio(DateTimeOffset.UtcNow);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> ConcluirExecucaoAsync(
        EndToEndId endToEndId,
        Guid pedidoId,
        ExternalEvidence evidencia,
        EstadoPedidoPix estadoFinal,
        string owner,
        int versaoEsperada,
        DominioEvento outboxEvento,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var agora = DateTimeOffset.UtcNow;
            var pedido = await _db.Pedidos.FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
            var registro = await _db.ProcessedIds.FirstOrDefaultAsync(r => r.EndToEndId == endToEndId, ct);

            // PIX-REV-02: Fencing por generation, owner e lease ativo no RegistroExecucao
            if (registro != null)
            {
                if (registro.Owner != owner || registro.ExpiresAt <= agora)
                {
                    await tx.RollbackAsync(ct);
                    _db.ChangeTracker.Clear();
                    return false;
                }
            }

            if (pedido != null)
            {
                if (pedido.Estado == estadoFinal)
                {
                    return true; // Idempotente
                }

                // PIX-REV-02: Validação estrita de versão, owner e lease ativo no Pedido
                if (pedido.Versao != versaoEsperada || (!string.IsNullOrEmpty(pedido.OwnerVigente) && pedido.OwnerVigente != owner) || (pedido.LeaseExpiresAt.HasValue && agora >= pedido.LeaseExpiresAt.Value))
                {
                    await tx.RollbackAsync(ct);
                    _db.ChangeTracker.Clear();
                    return false;
                }

                if (estadoFinal == EstadoPedidoPix.Confirmado)
                {
                    pedido.Confirmar(evidencia, versaoEsperada, owner, agora, registro?.Generation);
                }
                else if (estadoFinal == EstadoPedidoPix.RejeitadoDefinitivo)
                {
                    pedido.RejeitarDefinitivo(evidencia, versaoEsperada, owner, agora, registro?.Generation);
                }
                else if (estadoFinal == EstadoPedidoPix.ResultadoIncerto)
                {
                    pedido.MarcarResultadoIncerto(evidencia.Referencia, evidencia, agora);
                }
            }

            if (registro != null)
            {
                registro.Concluir(evidencia, estadoFinal, agora);
            }

            // Se financeiro, avança execução da conta
            if (pedido != null && pedido.SequenciaConta.HasValue)
            {
                var coord = await _db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == pedido.ContaId, ct);
                if (coord != null)
                {
                    if (estadoFinal == EstadoPedidoPix.Confirmado || estadoFinal == EstadoPedidoPix.RejeitadoDefinitivo)
                    {
                        coord.AvancarExecucao(pedido.SequenciaConta.Value, DateTimeOffset.UtcNow);
                        coord.Desbloquear(pedido.SequenciaConta.Value, "Incerteza", DateTimeOffset.UtcNow);
                    }
                    else if (estadoFinal == EstadoPedidoPix.ResultadoIncerto)
                    {
                        coord.Bloquear(pedido.SequenciaConta.Value, "Incerteza", DateTimeOffset.UtcNow);
                    }
                }
            }

            // PIX-AUD-04: Anexa Outbox usando o tipo concreto outboxEvento.GetType() para preservar todas as propriedades filhas (INV-12/13)
            var payload = JsonSerializer.Serialize(outboxEvento, outboxEvento.GetType());
            _db.OutboxRecords.Add(new OutboxRecord
            {
                EventId = outboxEvento.EventId,
                Tipo = outboxEvento.Tipo,
                SchemaVersion = outboxEvento.SchemaVersion,
                CorrelationId = outboxEvento.CorrelationId,
                PayloadJson = payload,
                Status = "PENDING",
                OccurredAt = outboxEvento.OccurredAt
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return false;
        }
        catch (Exception)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<RegistroExecucao?> ObterEstadoAsync(EndToEndId endToEndId, CancellationToken ct = default)
    {
        return await _db.ProcessedIds
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.EndToEndId == endToEndId, ct);
    }
}
