using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Pix.Processing.Application.Models;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.ValueObjects;
using StackExchange.Redis;

namespace Pix.Processing.Infrastructure.Adapters;

public class RedisAdmissionLimiter : IAdmissionLimiter
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly int _limitePorJanela;
    private readonly TimeSpan _duracaoJanela;
    private bool _simularIndisponibilidade;

    private const string SlidingWindowLuaScript = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local windowMs = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local nonce = ARGV[4]
        local clearBefore = now - windowMs

        redis.call('ZREMRANGEBYSCORE', key, '-inf', clearBefore)
        local current = redis.call('ZCARD', key)
        if current < limit then
            local member = now .. ':' .. nonce
            redis.call('ZADD', key, now, member)
            redis.call('EXPIRE', key, math.ceil(windowMs / 1000) + 2)
            return 1
        else
            return 0
        end";

    public RedisAdmissionLimiter(int limitePorJanela = 2, TimeSpan? duracaoJanela = null, IConnectionMultiplexer? redis = null)
    {
        _limitePorJanela = limitePorJanela;
        _duracaoJanela = duracaoJanela ?? TimeSpan.FromSeconds(10);
        _redis = redis;
    }

    public void SimularIndisponibilidade(bool indisponivel) => _simularIndisponibilidade = indisponivel;

    public async Task<AdmissaoResultado> VerificarAdmissaoAsync(ClienteId clienteId, string tipoOperacao, CancellationToken ct = default)
    {
        if (_simularIndisponibilidade)
        {
            return new AdmissaoResultado(StatusAdmissao.Indisponivel, "Redis temporariamente indisponível.");
        }

        // PIX-REV-06: Quando Redis for nulo ou desconectado, nega novas admissões (StatusAdmissao.Indisponivel),
        // sem fallback permissivo em memória (INV-18)
        if (_redis == null || !_redis.IsConnected)
        {
            return new AdmissaoResultado(StatusAdmissao.Indisponivel, "Redis desconectado ou não configurado.");
        }

        var chave = $"admissao:{clienteId.Valor}:{tipoOperacao}";
        var agora = DateTimeOffset.UtcNow;

        try
        {
            var db = _redis.GetDatabase();
            var nowMs = agora.ToUnixTimeMilliseconds();
            var windowMs = (long)_duracaoJanela.TotalMilliseconds;
            var nonce = Guid.NewGuid().ToString("N");

            var res = await db.ScriptEvaluateAsync(
                SlidingWindowLuaScript,
                new RedisKey[] { chave },
                new RedisValue[] { nowMs, windowMs, _limitePorJanela, nonce });

            var admitido = (int)res == 1;
            return admitido
                ? new AdmissaoResultado(StatusAdmissao.Admitido)
                : new AdmissaoResultado(StatusAdmissao.LimiteAtingido, "Limite de requisições por janela atingido.");
        }
        catch (Exception)
        {
            return new AdmissaoResultado(StatusAdmissao.Indisponivel, "Falha de comunicação com Redis.");
        }
    }
}

public class RedisFinancialCache : IFinancialCache
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly IMemoryCache _memoryCache;
    private readonly int _maxEntries;
    private readonly object _memLock = new();
    private bool _simularIndisponibilidade;

    private const string CasSetLuaScript = @"
        local genKey = KEYS[1]
        local dataKey = KEYS[2]
        local expectedGen = tonumber(ARGV[1])
        local payload = ARGV[2]
        local ttlSeconds = tonumber(ARGV[3])

        local currentGen = tonumber(redis.call('GET', genKey) or '1')
        if currentGen == expectedGen then
            redis.call('SETEX', dataKey, ttlSeconds, payload)
            return 1
        else
            return 0
        end";

    private const string IncrGenAndDelLuaScript = @"
        local genKey = KEYS[1]
        local dataKey = KEYS[2]
        redis.call('INCR', genKey)
        redis.call('DEL', dataKey)
        return 1";

    public RedisFinancialCache(IConnectionMultiplexer? redis = null, int maxEntries = 10_000)
        : this(redis, new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = Math.Max(1, maxEntries)
        }), maxEntries)
    {
    }

    public RedisFinancialCache(IConnectionMultiplexer? redis, IMemoryCache memoryCache, int maxEntries = 10_000)
    {
        _redis = redis;
        _memoryCache = memoryCache;
        _maxEntries = Math.Max(1, maxEntries);
    }

    public int MaxEntries => _maxEntries;

    public void SimularIndisponibilidade(bool indisponivel) => _simularIndisponibilidade = indisponivel;

    public async Task<CacheGeneration> ObterGeracaoAtualAsync(ContaId contaId, CancellationToken ct = default)
    {
        if (_simularIndisponibilidade) return new CacheGeneration(1);

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var genKey = $"conta:{contaId.Valor}:gen";
                var val = await db.StringGetAsync(genKey);
                if (val.IsNullOrEmpty)
                {
                    await db.StringSetAsync(genKey, 1);
                    return new CacheGeneration(1);
                }
                return new CacheGeneration((long)val);
            }
            catch
            {
                // Fallback para memória se redis falhar
            }
        }

        lock (_memLock)
        {
            var genKey = $"gen:{contaId.Valor}";
            if (!_memoryCache.TryGetValue(genKey, out long gen))
            {
                gen = 1;
                _memoryCache.Set(genKey, gen, new MemoryCacheEntryOptions { Size = 1 });
            }
            return new CacheGeneration(gen);
        }
    }

    public async Task<FinancialSnapshot?> ObterSnapshotAsync(ContaId contaId, CancellationToken ct = default)
    {
        if (_simularIndisponibilidade) return null;

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var dataKey = $"conta:{contaId.Valor}:snapshot";
                var val = await db.StringGetAsync(dataKey);
                if (!val.IsNullOrEmpty)
                {
                    return JsonSerializer.Deserialize<FinancialSnapshot>(val.ToString());
                }
                return null;
            }
            catch
            {
                // Fallback para memória
            }
        }

        lock (_memLock)
        {
            var snapKey = $"snap:{contaId.Valor}";
            if (_memoryCache.TryGetValue(snapKey, out (FinancialSnapshot Snapshot, long Geracao) item))
            {
                return item.Snapshot;
            }
            return null;
        }
    }

    public async Task<bool> PreencherCasAsync(ContaId contaId, FinancialSnapshot snapshot, CacheGeneration geracaoEsperada, CancellationToken ct = default)
    {
        if (_simularIndisponibilidade) return false;

        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var genKey = $"conta:{contaId.Valor}:gen";
                var dataKey = $"conta:{contaId.Valor}:snapshot";
                var json = JsonSerializer.Serialize(snapshot);
                var ttl = (int)Math.Max(1, (snapshot.ValidoAte - DateTimeOffset.UtcNow).TotalSeconds);

                var res = await db.ScriptEvaluateAsync(
                    CasSetLuaScript,
                    new RedisKey[] { genKey, dataKey },
                    new RedisValue[] { geracaoEsperada.Numero, json, ttl });

                return (int)res == 1;
            }
            catch
            {
                return false;
            }
        }

        lock (_memLock)
        {
            var genKey = $"gen:{contaId.Valor}";
            var curGen = _memoryCache.TryGetValue(genKey, out long g) ? g : 1;
            if (curGen != geracaoEsperada.Numero)
            {
                return false;
            }

            var snapKey = $"snap:{contaId.Valor}";
            var ttl = snapshot.ValidoAte - DateTimeOffset.UtcNow;
            var options = new MemoryCacheEntryOptions { Size = 1 };
            if (ttl > TimeSpan.Zero)
            {
                options.AbsoluteExpirationRelativeToNow = ttl;
            }

            _memoryCache.Set(snapKey, (snapshot, curGen), options);
            return true;
        }
    }

    public async Task IncrementarGeracaoERemoverAsync(ContaId contaId, CancellationToken ct = default)
    {
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var genKey = $"conta:{contaId.Valor}:gen";
                var dataKey = $"conta:{contaId.Valor}:snapshot";

                await db.ScriptEvaluateAsync(
                    IncrGenAndDelLuaScript,
                    new RedisKey[] { genKey, dataKey });
            }
            catch
            {
                // Fallback
            }
        }

        lock (_memLock)
        {
            var genKey = $"gen:{contaId.Valor}";
            long nextGen = 2;
            if (_memoryCache.TryGetValue(genKey, out long curGen))
            {
                nextGen = curGen + 1;
            }
            _memoryCache.Set(genKey, nextGen, new MemoryCacheEntryOptions { Size = 1 });
            _memoryCache.Remove($"snap:{contaId.Valor}");
        }
    }
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}

public class OutbankCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private int _consecutiveFailures;
    private DateTimeOffset _lastFailureTime;
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private readonly object _lock = new();

    public OutbankCircuitBreaker(int failureThreshold = 3, TimeSpan? openDuration = null)
    {
        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(5);
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open && (DateTimeOffset.UtcNow - _lastFailureTime) > _openDuration)
                {
                    _state = CircuitBreakerState.HalfOpen;
                }
                return _state;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _state = CircuitBreakerState.Closed;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTimeOffset.UtcNow;
            if (_consecutiveFailures >= _failureThreshold)
            {
                _state = CircuitBreakerState.Open;
            }
        }
    }
}

public class OutbankAclAdapter : IOutbankAcl
{
    private readonly ConcurrentDictionary<string, ExternalEvidence> _execucoesRegistradas = new();
    private readonly OutbankCircuitBreaker _circuitBreaker;
    private readonly Random _jitter = new();
    private bool _simularTimeout;
    private bool _simularFalha;
    private TimeSpan _timeoutSpi = TimeSpan.FromSeconds(2);
    private TimeSpan? _delaySimulado;

    public OutbankAclAdapter(OutbankCircuitBreaker? circuitBreaker = null)
    {
        _circuitBreaker = circuitBreaker ?? new OutbankCircuitBreaker();
    }

    public void ConfigurarTimeout(bool timeout) => _simularTimeout = timeout;
    public void ConfigurarFalha(bool falha) => _simularFalha = falha;
    public void ConfigurarTimeoutSpi(TimeSpan timeout) => _timeoutSpi = timeout;
    public void ConfigurarDelaySimulado(TimeSpan delay) => _delaySimulado = delay;
    public CircuitBreakerState EstadoCircuito => _circuitBreaker.State;

    public async Task<ExternalEvidence> ExecutarEnvioPixAsync(EndToEndId endToEndId, Money valor, ContaId origem, CancellationToken ct = default)
    {
        if (_circuitBreaker.State == CircuitBreakerState.Open)
        {
            throw new CircuitBreakerOpenException("Circuito Outbank está ABERTO para novas requisições.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeoutSpi);

        // Retry com jitter apenas para falhas transitórias pré-envio identificadas
        const int maxTentativas = 2;
        for (var tentativa = 1; tentativa <= maxTentativas; tentativa++)
        {
            try
            {
                if (_delaySimulado.HasValue)
                {
                    await Task.Delay(_delaySimulado.Value, cts.Token);
                }

                if (_simularTimeout)
                {
                    _circuitBreaker.RecordFailure();
                    // Timeout pós-envio não tem retry automático (INV-06, INV-19)
                    throw new TimeoutException($"Tempo limite excedido na comunicação com Outbank ({_timeoutSpi.TotalSeconds} segundos).");
                }

                if (_simularFalha)
                {
                    _circuitBreaker.RecordFailure();
                    throw new HttpRequestException("Falha de transporte HTTP transitória com Outbank.");
                }

                var evidencia = new ExternalEvidence(
                    "EnvioPix",
                    "LiquidacaoInstantanea",
                    "Outbank-Core",
                    $"TX-{endToEndId.Valor}",
                    DateTimeOffset.UtcNow,
                    sucesso: true);

                _execucoesRegistradas[endToEndId.Valor] = evidencia;
                _circuitBreaker.RecordSuccess();
                return evidencia;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _circuitBreaker.RecordFailure();
                throw new TimeoutException($"Tempo limite excedido na comunicação com Outbank ({_timeoutSpi.TotalSeconds} segundos).");
            }
            catch (HttpRequestException) when (tentativa < maxTentativas)
            {
                // Jitter proporcional com backoff exponencial
                var backoffMs = (int)(Math.Pow(2, tentativa) * 50) + _jitter.Next(10, 30);
                await Task.Delay(backoffMs, ct);
            }
            catch
            {
                throw;
            }
        }

        throw new HttpRequestException("Falha de comunicação após tentativas esgotadas.");
    }

    public Task<ExternalEvidence?> ConsultarPixAsync(EndToEndId endToEndId, CancellationToken ct = default)
    {
        if (_circuitBreaker.State == CircuitBreakerState.Open)
        {
            throw new CircuitBreakerOpenException("Circuito Outbank está ABERTO para consultas.");
        }

        if (_execucoesRegistradas.TryGetValue(endToEndId.Valor, out var evidencia))
        {
            _circuitBreaker.RecordSuccess();
            return Task.FromResult<ExternalEvidence?>(evidencia);
        }

        return Task.FromResult<ExternalEvidence?>(null);
    }
}
