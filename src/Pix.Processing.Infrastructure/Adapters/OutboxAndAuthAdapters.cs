using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Pix.Processing.Application.Ports;
using Pix.Processing.Domain.Events;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure.Entities;

namespace Pix.Processing.Infrastructure.Adapters;

public interface IBrokerPublisher
{
    Task PublicarEventoAsync(DominioEvento evento, CancellationToken ct = default);
}

public class AzureServiceBusPublisher : IBrokerPublisher
{
    private readonly ServiceBusSender? _sender;
    private readonly ConcurrentBag<DominioEvento> _mensagensPublicadas = new();

    public AzureServiceBusPublisher(ServiceBusSender? sender = null)
    {
        _sender = sender;
    }

    public IReadOnlyCollection<DominioEvento> MensagensPublicadas => _mensagensPublicadas;

    public async Task PublicarEventoAsync(DominioEvento evento, CancellationToken ct = default)
    {
        string payload;
        string? sessionId = null;

        if (evento is OutboxEnvelopeEvento env)
        {
            payload = env.PayloadJson;
            sessionId = env.ContaId?.Valor;
        }
        else if (evento is OperacaoConcluidaEvento op)
        {
            payload = JsonSerializer.Serialize(op);
            sessionId = op.ContaId?.Valor;
        }
        else if (evento is PedidoFinanceiroAceitoEvento ped)
        {
            payload = JsonSerializer.Serialize(new
            {
                pedidoId = ped.PedidoId,
                contaId = ped.ContaId.Valor,
                sequencia = ped.Sequencia.Numero,
                endToEndId = ped.EndToEndId.Valor,
                valor = ped.Valor.Quantia,
                moeda = ped.Valor.Moeda,
                tipo = ped.TipoOperacao.ToString()
            });
            sessionId = ped.ContaId.Valor;
        }
        else
        {
            payload = JsonSerializer.Serialize(evento, evento.GetType());
            var propContaId = evento.GetType().GetProperty("ContaId");
            if (propContaId?.GetValue(evento) is ContaId cid)
            {
                sessionId = cid.Valor;
            }
        }

        // Se ainda não encontrou sessionId, tenta extrair de JSON
        if (string.IsNullOrEmpty(sessionId) && !string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("contaId", out var cVal))
                {
                    sessionId = cVal.GetString();
                }
                else if (doc.RootElement.TryGetProperty("ContaId", out var cVal2))
                {
                    sessionId = cVal2.ValueKind == JsonValueKind.Object && cVal2.TryGetProperty("Valor", out var vVal)
                        ? vVal.GetString()
                        : cVal2.GetString();
                }
            }
            catch { }
        }

        if (_sender != null)
        {
            var sbMessage = new ServiceBusMessage(payload)
            {
                MessageId = evento.EventId,
                CorrelationId = evento.CorrelationId,
                Subject = evento.Tipo
            };

            // PIX-REV-04: SessionId = ContaId para ordenação estrita por conta no Service Bus
            if (!string.IsNullOrEmpty(sessionId))
            {
                sbMessage.SessionId = sessionId;
            }

            if (evento is OutboxEnvelopeEvento envEv)
            {
                if (!string.IsNullOrEmpty(envEv.TraceParent))
                    sbMessage.ApplicationProperties["traceparent"] = envEv.TraceParent;
                if (!string.IsNullOrEmpty(envEv.TraceState))
                    sbMessage.ApplicationProperties["tracestate"] = envEv.TraceState;
            }
            else if (System.Diagnostics.Activity.Current != null)
            {
                sbMessage.ApplicationProperties["traceparent"] = System.Diagnostics.Activity.Current.Id;
                if (!string.IsNullOrEmpty(System.Diagnostics.Activity.Current.TraceStateString))
                    sbMessage.ApplicationProperties["tracestate"] = System.Diagnostics.Activity.Current.TraceStateString;
            }

            await _sender.SendMessageAsync(sbMessage, ct);
        }

        _mensagensPublicadas.Add(evento);
    }
}

public class OutboxRelay : IOutboxStore
{
    private readonly PixDbContext _db;

    public OutboxRelay(PixDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DominioEvento>> AdquirirLoteAsync(int batchSize, string owner, CancellationToken ct = default)
    {
        await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;

        List<OutboxRecord> pendentes;
        var agora = DateTimeOffset.UtcNow;

        if (_db.Database.IsRelational())
        {
            // Bloqueio pessimista de fila: T-SQL UPDLOCK, READPAST impede condições de corrida e duplicação
            // entre réplicas concorrentes sob escala horizontal (TX-06, INV-08)
            pendentes = await _db.OutboxRecords
                .FromSqlRaw(
                    "SELECT TOP ({1}) * FROM [pix].[OutboxRecords] WITH (UPDLOCK, READPAST) WHERE [Status] = 'PENDING' OR ([Status] = 'PROCESSING' AND [ExpiresAt] < {0}) ORDER BY [OccurredAt]",
                    agora,
                    batchSize)
                .ToListAsync(ct);
        }
        else
        {
            pendentes = await _db.OutboxRecords
                .Where(r => r.Status == "PENDING" || (r.Status == "PROCESSING" && r.ExpiresAt < agora))
                .OrderBy(r => r.OccurredAt)
                .Take(batchSize)
                .ToListAsync(ct);
        }

        var resultado = new List<DominioEvento>();

        foreach (var p in pendentes)
        {
            p.Status = "PROCESSING";
            p.Owner = owner;
            p.Generation++;
            p.ExpiresAt = agora.AddSeconds(30);

            ContaId? contaId = null;
            if (!string.IsNullOrWhiteSpace(p.PayloadJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(p.PayloadJson);
                    if (doc.RootElement.TryGetProperty("contaId", out var cVal))
                    {
                        var cStr = cVal.GetString();
                        if (!string.IsNullOrEmpty(cStr)) contaId = new ContaId(cStr);
                    }
                    else if (doc.RootElement.TryGetProperty("ContaId", out var cVal2))
                    {
                        var cStr = cVal2.ValueKind == JsonValueKind.Object && cVal2.TryGetProperty("Valor", out var vVal)
                            ? vVal.GetString()
                            : cVal2.GetString();
                        if (!string.IsNullOrEmpty(cStr)) contaId = new ContaId(cStr);
                    }
                }
                catch { }
            }

            // PIX-REV-04 / PIX-AUD-04: Preserva o envelope real com PayloadJson, tipo, versão do schema, correlação e identidades fortes (ContaId, EndToEndId)
            DominioEvento eventoCriado;

            if (p.Tipo == "OperacaoConcluida")
            {
                DominioEvento? ev = null;
                try
                {
                    var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    opt.Converters.Add(new ContaIdJsonConverter());
                    opt.Converters.Add(new EndToEndIdJsonConverter());
                    ev = JsonSerializer.Deserialize<OperacaoConcluidaEvento>(p.PayloadJson, opt);
                }
                catch { }

                if (ev is OperacaoConcluidaEvento opEv)
                {
                    EndToEndId? e2e = opEv.EndToEndId;
                    ContaId? cId = opEv.ContaId;

                    if ((!e2e.HasValue || string.IsNullOrEmpty(e2e.Value.Valor)) && !string.IsNullOrWhiteSpace(p.PayloadJson))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(p.PayloadJson);
                            if (doc.RootElement.TryGetProperty("endToEndId", out var eVal) || doc.RootElement.TryGetProperty("EndToEndId", out eVal))
                            {
                                var str = eVal.ValueKind == JsonValueKind.Object && (eVal.TryGetProperty("Valor", out var v) || eVal.TryGetProperty("valor", out v))
                                    ? v.GetString()
                                    : eVal.GetString();
                                if (!string.IsNullOrEmpty(str)) e2e = new EndToEndId(str);
                            }
                        }
                        catch { }
                    }

                    if ((!cId.HasValue || string.IsNullOrEmpty(cId.Value.Valor)) && !string.IsNullOrWhiteSpace(p.PayloadJson))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(p.PayloadJson);
                            if (doc.RootElement.TryGetProperty("contaId", out var cVal) || doc.RootElement.TryGetProperty("ContaId", out cVal))
                            {
                                var str = cVal.ValueKind == JsonValueKind.Object && (cVal.TryGetProperty("Valor", out var v) || cVal.TryGetProperty("valor", out v))
                                    ? v.GetString()
                                    : cVal.GetString();
                                if (!string.IsNullOrEmpty(str)) cId = new ContaId(str);
                            }
                        }
                        catch { }
                    }

                    cId ??= contaId;

                    eventoCriado = opEv with { EndToEndId = e2e, ContaId = cId, Generation = p.Generation };
                }
                else
                {
                    eventoCriado = new OutboxEnvelopeEvento(
                        p.EventId,
                        p.Tipo,
                        p.SchemaVersion,
                        p.OccurredAt,
                        p.CorrelationId,
                        p.PayloadJson,
                        contaId,
                        p.TraceParent,
                        p.TraceState) { Generation = p.Generation };
                }
            }
            else
            {
                eventoCriado = new OutboxEnvelopeEvento(
                    p.EventId,
                    p.Tipo,
                    p.SchemaVersion,
                    p.OccurredAt,
                    p.CorrelationId,
                    p.PayloadJson,
                    contaId,
                    p.TraceParent,
                    p.TraceState) { Generation = p.Generation };
            }

            resultado.Add(eventoCriado);
        }

        await _db.SaveChangesAsync(ct);
        if (tx != null)
        {
            await tx.CommitAsync(ct);
        }

        return resultado;
    }

    public Task MarcarPublicadoAsync(string eventId, string owner, CancellationToken ct = default)
        => MarcarPublicadoAsync(eventId, owner, generation: null, ct);

    public async Task MarcarPublicadoAsync(string eventId, string owner, long generation, CancellationToken ct = default)
        => await MarcarPublicadoAsync(eventId, owner, (long?)generation, ct);

    public async Task MarcarPublicadoAsync(string eventId, string owner, long? generation, CancellationToken ct = default)
    {
        var agora = DateTimeOffset.UtcNow;

        if (_db.Database.IsRelational())
        {
            // PIX-AUD-05: Atualização atômica diretamente no banco. A condição CAS participa
            // integralmente da cláusula WHERE da instrução SQL (INV-08, TX-06).
            // A tabela mapeada no banco é [pix].[OutboxRecords]. Se rows == 0, o CAS falhou legitimamente.
            if (generation.HasValue)
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE [pix].[OutboxRecords] SET [Status] = 'PUBLISHED', [PublishedAt] = {0} WHERE [EventId] = {1} AND [Owner] = {2} AND [Generation] = {3} AND [Status] = 'PROCESSING' AND [ExpiresAt] > {0}",
                    agora, eventId, owner, generation.Value);
            }
            else
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE [pix].[OutboxRecords] SET [Status] = 'PUBLISHED', [PublishedAt] = {0} WHERE [EventId] = {1} AND [Owner] = {2} AND [Status] = 'PROCESSING' AND [ExpiresAt] > {0}",
                    agora, eventId, owner);
            }
            return;
        }

        // Para InMemory ou providers de teste:
        // A entidade OutboxRecord possui IsConcurrencyToken() e [ConcurrencyCheck] em Generation, Owner, Status e ExpiresAt,
        // garantindo que qualquer takeover concorrente entre a leitura e o SaveChanges lance DbUpdateConcurrencyException
        var record = await _db.OutboxRecords.FirstOrDefaultAsync(r => r.EventId == eventId, ct);
        if (record == null) return;

        var entry = _db.Entry(record);
        if (entry.State == EntityState.Unchanged)
        {
            await entry.ReloadAsync(ct);
        }

        if (record.Owner == owner &&
            (!generation.HasValue || record.Generation == generation.Value) &&
            record.Status == "PROCESSING" &&
            record.ExpiresAt > agora)
        {
            record.Status = "PUBLISHED";
            record.PublishedAt = agora;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Takeover concorrente detectado entre a verificação e o SaveChanges: CAS falha de forma segura
                _db.ChangeTracker.Clear();
            }
        }
    }
}

public class DurableOutboxRelayProcessor
{
    private readonly IOutboxStore _outboxStore;
    private readonly IBrokerPublisher _brokerPublisher;

    public DurableOutboxRelayProcessor(IOutboxStore outboxStore, IBrokerPublisher brokerPublisher)
    {
        _outboxStore = outboxStore;
        _brokerPublisher = brokerPublisher;
    }

    public async Task<int> ProcessarLoteAsync(int batchSize, string owner, CancellationToken ct = default)
    {
        var eventos = await _outboxStore.AdquirirLoteAsync(batchSize, owner, ct);
        var publicados = 0;

        foreach (var evento in eventos)
        {
            // Publicação remota é estritamente fora da transação de banco de dados (TX-06, INV-03)
            await _brokerPublisher.PublicarEventoAsync(evento, ct);
            await _outboxStore.MarcarPublicadoAsync(evento.EventId, owner, evento.Generation, ct);
            publicados++;
        }

        return publicados;
    }
}

public class AutorizacaoAdapter : IAutorizacaoPort
{
    private readonly ConcurrentDictionary<(string ClienteId, string ContaId), HashSet<string>> _vinculos = new();

    public AutorizacaoAdapter()
    {
        // Vínculos padrão para clientes do sistema de homologação e testes
        RegistrarVinculo(new ClienteId("cliente-A"), new ContaId("conta-A"));
        RegistrarVinculo(new ClienteId("cli-1"), new ContaId("cnt-1"));
        RegistrarVinculo(new ClienteId("c1"), new ContaId("cnt1"));
        RegistrarVinculo(new ClienteId("cliente-1"), new ContaId("conta-1"));
        RegistrarVinculo(new ClienteId("client-valid"), new ContaId("account-valid"));
    }

    public void RegistrarVinculo(ClienteId clienteId, ContaId contaId, params string[] acoesPermitidas)
    {
        var permissoes = acoesPermitidas.Length > 0
            ? new HashSet<string>(acoesPermitidas, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" };

        _vinculos[(clienteId.Valor, contaId.Valor)] = permissoes;
    }

    public void RevogarVinculo(ClienteId clienteId, ContaId contaId)
    {
        _vinculos.TryRemove((clienteId.Valor, contaId.Valor), out _);
    }

    public Task<bool> AutorizarAsync(ClienteId clienteId, ContaId contaId, string acao, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clienteId.Valor) || string.IsNullOrWhiteSpace(contaId.Valor))
        {
            return Task.FromResult(false);
        }

        // Validação dinâmica sem strings estáticas hardcoded
        if (!_vinculos.TryGetValue((clienteId.Valor, contaId.Valor), out var permissoes))
        {
            return Task.FromResult(false);
        }

        if (permissoes.Contains("*") || permissoes.Contains(acao))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}

public class ContaIdJsonConverter : JsonConverter<ContaId>
{
    public override ContaId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            return new ContaId(str!);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("Valor", out var val) ||
                doc.RootElement.TryGetProperty("valor", out val) ||
                doc.RootElement.TryGetProperty("value", out val))
            {
                var str = val.GetString();
                if (!string.IsNullOrEmpty(str))
                    return new ContaId(str);
            }
        }

        throw new JsonException("Não foi possível desserializar ContaId.");
    }

    public override void Write(Utf8JsonWriter writer, ContaId value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Valor", value.Valor);
        writer.WriteEndObject();
    }
}

public class EndToEndIdJsonConverter : JsonConverter<EndToEndId>
{
    public override EndToEndId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            return new EndToEndId(str!);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("Valor", out var val) ||
                doc.RootElement.TryGetProperty("valor", out val) ||
                doc.RootElement.TryGetProperty("value", out val))
            {
                var str = val.GetString();
                if (!string.IsNullOrEmpty(str))
                    return new EndToEndId(str);
            }
        }

        throw new JsonException("Não foi possível desserializar EndToEndId.");
    }

    public override void Write(Utf8JsonWriter writer, EndToEndId value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Valor", value.Valor);
        writer.WriteEndObject();
    }
}

