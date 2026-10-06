using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Features.FallbackReadBalance;

public class FallbackReadBalanceUseCase
{
    private readonly IReadOnlyReplicaRepository _readReplicaRepo;
    private readonly ILocalCachePort _cacheLocal;

    public FallbackReadBalanceUseCase(IReadOnlyReplicaRepository readReplicaRepo, ILocalCachePort cacheLocal)
    {
        _readReplicaRepo = readReplicaRepo;
        _cacheLocal = cacheLocal;
    }

    public async Task<SaldoInfo?> ConsultarSaldoAsync(ContaId contaId, CancellationToken ct = default)
    {
        if (_cacheLocal.TryGetValue(contaId.Valor, out SaldoInfo? cached) && cached != null)
        {
            return cached;
        }

        var saldo = await _readReplicaRepo.ObterSaldoAsync(contaId, ct);
        if (saldo != null)
        {
            _cacheLocal.Set(contaId.Valor, saldo, TimeSpan.FromSeconds(30));
        }

        return saldo;
    }
}
