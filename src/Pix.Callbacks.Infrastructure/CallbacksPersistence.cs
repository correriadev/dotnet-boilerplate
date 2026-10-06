using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Pix.Callbacks.Application;
using Pix.Callbacks.Domain;

namespace Pix.Callbacks.Infrastructure;

public class CallbacksDbContext : DbContext
{
    public DbSet<CallbackDelivery> Entregas { get; set; } = null!;

    public CallbacksDbContext(DbContextOptions<CallbacksDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CallbackDelivery>(entity =>
        {
            entity.ToTable("callback_deliveries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasConversion(v => v.Valor, v => new DeliveryId(v));
            entity.HasIndex(e => new { e.EventoConclusaoId, e.DestinatarioId }).IsUnique();
            entity.Property(e => e.Versao).IsConcurrencyToken();
        });
    }
}

public class CallbackDeliveryStore : ICallbackDeliveryStore
{
    private readonly CallbacksDbContext _db;

    public CallbackDeliveryStore(CallbacksDbContext db)
    {
        _db = db;
    }

    public async Task<bool> AgendarEntregaAsync(CallbackDelivery entrega, CancellationToken ct = default)
    {
        try
        {
            var jaExiste = await _db.Entregas.AnyAsync(e => e.EventoConclusaoId == entrega.EventoConclusaoId && e.DestinatarioId == entrega.DestinatarioId, ct);
            if (jaExiste)
            {
                return false;
            }

            _db.Entregas.Add(entrega);
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // UNIQUE (EventoConclusaoId, DestinatarioId) conflito: deduplicação idempotente
            return false;
        }
    }

    public async Task<IReadOnlyList<CallbackDelivery>> ObterEntregasPendentesAsync(int limite, CancellationToken ct = default)
    {
        var agora = DateTimeOffset.UtcNow;
        return await _db.Entregas
            .Where(e => (e.Estado == EstadoEntrega.Pendente || e.Estado == EstadoEntrega.AguardandoRetry) && e.ProximaTentativa <= agora)
            .OrderBy(e => e.ProximaTentativa)
            .Take(limite)
            .ToListAsync(ct);
    }

    public async Task AtualizarEntregaAsync(CallbackDelivery entrega, CancellationToken ct = default)
    {
        _db.Entregas.Update(entrega);
        await _db.SaveChangesAsync(ct);
    }
}

public class SafeCallbackTransport : ISafeCallbackTransport
{
    private readonly HttpClient _httpClient;

    public SafeCallbackTransport(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, // INV-15: Prevenção contra open redirect malicioso
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var host = context.DnsEndPoint.Host;
                    CallbackDestination.ValidarHostNaoPrivado(host);

                    IPAddress targetIp;
                    if (IPAddress.TryParse(host, out var parsedIp))
                    {
                        CallbackDestination.ValidarIpAddress(parsedIp);
                        targetIp = parsedIp;
                    }
                    else
                    {
                        IPAddress[] ips;
                        try
                        {
                            ips = await Dns.GetHostAddressesAsync(host, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            throw new HttpRequestException($"Falha na resolução de DNS para {host}: {ex.Message}", ex);
                        }

                        if (ips.Length == 0)
                        {
                            throw new HttpRequestException($"Nenhum endereço IP resolvido para o host {host}.");
                        }

                        foreach (var ip in ips)
                        {
                            CallbackDestination.ValidarIpAddress(ip);
                        }

                        // PIX-REV-07: Pinos a conexão diretamente no IP validado para prevenir DNS rebinding
                        targetIp = ips[0];
                    }

                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(targetIp, context.DnsEndPoint.Port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
            _httpClient = new HttpClient(handler);
        }
    }

    public async Task<bool> EnviarAsync(CallbackDestination destino, string payloadJson, CancellationToken ct = default)
    {
        // Validação estrita do host com checagem de DNS no momento do envio (INV-15)
        await CallbackDestination.ValidarHostNaoPrivadoComDnsAsync(destino.Url.Host, ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, destino.Url);
        request.Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-Auth-Reference", destino.AuthReference);

        using var response = await _httpClient.SendAsync(request, ct);

        // Se redirecionar (301, 302, 307, 308), valida o destino do redirect antes de seguir
        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 && response.Headers.Location != null)
        {
            var redirectUrl = response.Headers.Location;
            if (!redirectUrl.IsAbsoluteUri)
                redirectUrl = new Uri(destino.Url, redirectUrl);

            if (!redirectUrl.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Redirect para protocolo não seguro (não-HTTPS) é proibido.");

            await CallbackDestination.ValidarHostNaoPrivadoComDnsAsync(redirectUrl.Host, ct);
            return false; // Não propaga credencial nem envia cegamente ao redirect (INV-15)
        }

        return response.IsSuccessStatusCode;
    }
}
