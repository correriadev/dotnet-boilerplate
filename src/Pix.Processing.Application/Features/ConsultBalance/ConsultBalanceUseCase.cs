using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Features.ConsultBalance;

public interface IFonteFinanceiraAutoritativa
{
    Task<(Money Saldo, Money Limite)> ConsultarOrigemAsync(ContaId contaId, CancellationToken ct = default);
}

public class ConsultBalanceUseCase
{
    private readonly IFinancialCache _cache;
    private readonly IFonteFinanceiraAutoritativa _origemAutoritativa;
    private readonly IAutorizacaoPort _autorizacaoPort;

    public ConsultBalanceUseCase(
        IFinancialCache cache,
        IFonteFinanceiraAutoritativa origemAutoritativa,
        IAutorizacaoPort autorizacaoPort)
    {
        _cache = cache;
        _origemAutoritativa = origemAutoritativa;
        _autorizacaoPort = autorizacaoPort;
    }

    public async Task<FinancialSnapshot?> ConsultarAsync(ClienteId clienteId, ContaId contaId, CancellationToken ct = default)
    {
        // 1. Autorização prévia (INV-01)
        var autorizado = await _autorizacaoPort.AutorizarAsync(clienteId, contaId, "ConsultarSaldo", ct);
        if (!autorizado)
            return null;

        var agora = DateTimeOffset.UtcNow;

        // 2. Tentar leitura do cache (UC-18)
        var cacheSnapshot = await _cache.ObterSnapshotAsync(contaId, ct);
        if (cacheSnapshot != null && cacheSnapshot.ValidoAte > agora)
        {
            return cacheSnapshot;
        }

        // 3. Cache-aside CAS: Captura geração antes da leitura autoritativa (INV-17)
        var geracaoCapturada = await _cache.ObterGeracaoAtualAsync(contaId, ct);

        // 4. Consulta fonte autoritativa
        var (saldo, limite) = await _origemAutoritativa.ConsultarOrigemAsync(contaId, ct);

        var novoSnapshot = new FinancialSnapshot(
            contaId,
            saldo,
            limite,
            agora,
            geracaoCapturada,
            agora.AddSeconds(60)); // TTL 60s

        // 5. Preencher cache condicionalmente por Compare-And-Set (INV-17)
        await _cache.PreencherCasAsync(contaId, novoSnapshot, geracaoCapturada, ct);

        return novoSnapshot;
    }

    public async Task InvalidarAsync(ContaId contaId, CancellationToken ct = default)
    {
        // UC-19: incrementa geração e remove cache (INV-17)
        await _cache.IncrementarGeracaoERemoverAsync(contaId, ct);
    }
}
