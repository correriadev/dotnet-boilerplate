using System.Collections.Concurrent;
using Pix.Processing.Application.Ports;

namespace Pix.Processing.Infrastructure.Adapters;

public class LocalMemoryCacheAdapter : ILocalCachePort
{
    private readonly ConcurrentDictionary<string, (object Value, DateTimeOffset ExpiresAt, long AcessadoEm)> _cache = new();
    private readonly int _capacidadeMaxima;
    private long _contadorAcesso;

    public LocalMemoryCacheAdapter(int capacidadeMaxima = 10_000)
    {
        _capacidadeMaxima = Math.Max(1, capacidadeMaxima);
    }

    public int CapacidadeMaxima => _capacidadeMaxima;
    public int Count => _cache.Count;

    public bool TryGetValue<T>(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out var item))
        {
            if (DateTimeOffset.UtcNow <= item.ExpiresAt)
            {
                _cache[key] = (item.Value, item.ExpiresAt, Interlocked.Increment(ref _contadorAcesso));
                value = (T)item.Value;
                return true;
            }
            _cache.TryRemove(key, out _);
        }

        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        if (value == null) return;

        if (_cache.Count >= _capacidadeMaxima && !_cache.ContainsKey(key))
        {
            ExpurgarExcedentes();
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        var accessTime = Interlocked.Increment(ref _contadorAcesso);
        _cache[key] = (value, expiresAt, accessTime);
    }

    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void ExpurgarExcedentes()
    {
        var agora = DateTimeOffset.UtcNow;
        foreach (var pair in _cache)
        {
            if (agora > pair.Value.ExpiresAt)
            {
                _cache.TryRemove(pair.Key, out _);
            }
        }

        while (_cache.Count >= _capacidadeMaxima)
        {
            var maisAntiga = _cache.OrderBy(kv => kv.Value.AcessadoEm).FirstOrDefault();
            if (maisAntiga.Key != null)
            {
                _cache.TryRemove(maisAntiga.Key, out _);
            }
            else
            {
                break;
            }
        }
    }
}

public record ResilientCacheResult(bool Sucesso, bool CircuitoAberto, int StatusCode, string? Mensagem);

public class ResilientCacheAdapter
{
    private readonly IReadOnlyReplicaRepository _replicaRepo;
    private readonly ILocalCachePort _localCache;
    private readonly OutbankCircuitBreaker _circuitBreaker;
    private bool _simularFalhaRedis;

    public ResilientCacheAdapter(
        IReadOnlyReplicaRepository replicaRepo,
        ILocalCachePort localCache,
        int failureThreshold = 3,
        TimeSpan? openDuration = null)
    {
        _replicaRepo = replicaRepo;
        _localCache = localCache;
        _circuitBreaker = new OutbankCircuitBreaker(failureThreshold, openDuration ?? TimeSpan.FromSeconds(5));
    }

    public CircuitBreakerState EstadoCircuito => _circuitBreaker.State;

    public void SimularFalhaRedis(bool falha) => _simularFalhaRedis = falha;

    public Task<ResilientCacheResult> TentarOperacaoComProtecaoAsync()
    {
        if (_circuitBreaker.State == CircuitBreakerState.Open)
        {
            return Task.FromResult(new ResilientCacheResult(
                Sucesso: false,
                CircuitoAberto: true,
                StatusCode: 503,
                Mensagem: "Circuito aberto: Redis temporariamente indisponível. Requisição barrada para proteção da infraestrutura."));
        }

        if (_simularFalhaRedis)
        {
            _circuitBreaker.RecordFailure();
            return Task.FromResult(new ResilientCacheResult(
                Sucesso: false,
                CircuitoAberto: _circuitBreaker.State == CircuitBreakerState.Open,
                StatusCode: _circuitBreaker.State == CircuitBreakerState.Open ? 503 : 500,
                Mensagem: "Falha de conectividade com cluster Redis."));
        }

        _circuitBreaker.RecordSuccess();
        return Task.FromResult(new ResilientCacheResult(
            Sucesso: true,
            CircuitoAberto: false,
            StatusCode: 200,
            Mensagem: "Operação executada com sucesso."));
    }
}
