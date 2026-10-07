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

public class AcceptanceStore : IAcceptanceStore
{
    private sealed class RefCountedLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount = 1;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RefCountedLock> _accountLocks = new();
    private readonly PixDbContext _db;

    public AcceptanceStore(PixDbContext db)
    {
        _db = db;
    }

    public async Task<AceiteOriginal?> ObterAceitePorChaveAsync(IdempotencyKey chave, CancellationToken ct = default)
    {
        var record = await _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ChaveComposta == chave.ChaveComposta, ct);

        if (record == null)
            return null;

        return new AceiteOriginal(
            record.PedidoId,
            record.StatusCode,
            record.HeadersJson,
            record.CorpoJson,
            new Fingerprint(record.FingerprintVersao, record.FingerprintHash),
            record.CriadoEm);
    }

    public async Task<AceiteResultado> ConfirmarAceiteAsync(
        PedidoPix pedido,
        IdempotencyKey chave,
        Fingerprint fingerprint,
        Func<Guid, AceiteOriginal> factoryAceite,
        bool ehFinanceiro,
        CancellationToken ct = default)
    {
        RefCountedLock? lockEntry = null;
        if (ehFinanceiro)
        {
            lock (_accountLocks)
            {
                if (_accountLocks.TryGetValue(pedido.ContaId.Valor, out var existing))
                {
                    existing.RefCount++;
                    lockEntry = existing;
                }
                else
                {
                    lockEntry = new RefCountedLock();
                    _accountLocks[pedido.ContaId.Valor] = lockEntry;
                }
            }
        }

        bool lockAcquired = false;
        if (lockEntry != null)
        {
            await lockEntry.Semaphore.WaitAsync(ct);
            lockAcquired = true;
        }

        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                AccountSequence? seq = null;

                // Se for operação financeira, atribui sequência ordenada da conta (TX-01, INV-04)
                if (ehFinanceiro)
                {
                    var coord = await _db.CoordenacoesConta
                        .FirstOrDefaultAsync(c => c.ContaId == pedido.ContaId, ct);

                    if (coord == null)
                    {
                        coord = new CoordenacaoConta(pedido.ContaId, 1, DateTimeOffset.UtcNow);
                        _db.CoordenacoesConta.Add(coord);
                    }

                    if (coord.Estado == EstadoCoordenacao.Bloqueada)
                    {
                        await tx.RollbackAsync(ct);
                        return new AceiteResultado(StatusResultadoAceite.ConflitoDeConteudo, null, "Conta está bloqueada para novas operações financeiras.");
                    }

                    seq = coord.AtribuirProximaSequencia(DateTimeOffset.UtcNow);
                    pedido.AtribuirSequenciaConta(seq.Value.Numero);
                }

                var aceite = factoryAceite(pedido.Id);

                // 1. Inserir Pedido
                _db.Pedidos.Add(pedido);

                // 2. Inserir Idempotência
                _db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    ChaveComposta = chave.ChaveComposta,
                    ClienteId = chave.ClienteId.Valor,
                    TipoOperacao = chave.TipoOperacao,
                    Chave = chave.Chave,
                    FingerprintHash = fingerprint.Hash,
                    FingerprintVersao = fingerprint.Versao,
                    PedidoId = pedido.Id,
                    StatusCode = aceite.StatusCode,
                    HeadersJson = aceite.HeadersJson,
                    CorpoJson = aceite.CorpoJson,
                    CriadoEm = aceite.CriadoEm
                });

                // 3. Inserir Outbox
                var outboxPayload = JsonSerializer.Serialize(new
                {
                    pedidoId = pedido.Id,
                    contaId = pedido.ContaId.Valor,
                    sequencia = seq?.Numero,
                    endToEndId = pedido.EndToEndId?.Valor,
                    valor = pedido.Valor.Quantia,
                    moeda = pedido.Valor.Moeda,
                    tipo = pedido.TipoOperacao.ToString()
                });

                _db.OutboxRecords.Add(new OutboxRecord
                {
                    EventId = Guid.NewGuid().ToString(),
                    Tipo = ehFinanceiro ? "PedidoFinanceiroAceito" : "GeracaoCobrancaSolicitada",
                    SchemaVersion = "1.0",
                    CorrelationId = pedido.Id.ToString(),
                    PayloadJson = outboxPayload,
                    Status = "PENDING",
                    OccurredAt = DateTimeOffset.UtcNow,
                    TraceParent = System.Diagnostics.Activity.Current?.Id,
                    TraceState = System.Diagnostics.Activity.Current?.TraceStateString
                });

                // Commit atômico conjunto (INV-03, TX-01)
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return new AceiteResultado(StatusResultadoAceite.Criado, aceite);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();

                var vencedor = await ObterAceitePorChaveAsync(chave, ct);
                if (vencedor != null)
                {
                    if (vencedor.Fingerprint == fingerprint)
                        return new AceiteResultado(StatusResultadoAceite.Replay, vencedor);
                    else
                        return new AceiteResultado(StatusResultadoAceite.ConflitoDeConteudo, null, "Conflito: Chave duplicada com fingerprint divergente.");
                }

                return new AceiteResultado(StatusResultadoAceite.ResultadoDeCommitIncerto, null, ex.Message);
            }
        });
        }
        finally
        {
            if (lockEntry != null)
            {
                if (lockAcquired)
                {
                    lockEntry.Semaphore.Release();
                }

                lock (_accountLocks)
                {
                    lockEntry.RefCount--;
                    if (lockEntry.RefCount <= 0)
                    {
                        _accountLocks.TryRemove(pedido.ContaId.Valor, out _);
                        lockEntry.Semaphore.Dispose();
                    }
                }
            }
        }
    }
}
