using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Ports;

public record SaldoInfo(ContaId ContaId, decimal SaldoDisponivel, decimal LimiteDiario, string Moeda = "BRL")
{
    public decimal LimiteCredito => LimiteDiario;
}

public interface IReadOnlyReplicaRepository
{
    string ConnectionString { get; }
    Task<SaldoInfo?> ObterSaldoAsync(ContaId contaId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> ObterCatalogoAsync(CancellationToken ct = default);
    Task ExecutarMutacaoNaoPermitidaAsync(string sql, CancellationToken ct = default);
}
