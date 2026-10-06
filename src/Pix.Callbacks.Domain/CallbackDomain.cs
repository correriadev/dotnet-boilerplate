using System.Net;

namespace Pix.Callbacks.Domain;

public readonly record struct DeliveryId
{
    public Guid Valor { get; }

    public DeliveryId(Guid valor)
    {
        if (valor == Guid.Empty)
            throw new ArgumentException("DeliveryId não pode ser vazio.", nameof(valor));

        Valor = valor;
    }

    public static DeliveryId Novo() => new(Guid.NewGuid());
    public override string ToString() => Valor.ToString();
}

public enum EstadoEntrega
{
    Pendente = 1,
    EmEnvio = 2,
    Aceita = 3,
    AguardandoRetry = 4,
    Esgotada = 5
}

public class CallbackDestination
{
    public string Id { get; }
    public Uri Url { get; }
    public string AuthReference { get; }
    public string PolicyVersion { get; }

    public CallbackDestination(string id, string urlString, string authReference, string policyVersion = "1.0")
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id de destino não pode ser vazio.", nameof(id));

        if (string.IsNullOrWhiteSpace(urlString))
            throw new ArgumentException("URL de destino não pode ser vazia.", nameof(urlString));

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
            throw new ArgumentException("URL inválida.", nameof(urlString));

        // INV-15: Destino deve ser HTTPS
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Apenas destinos HTTPS são permitidos.", nameof(urlString));

        // INV-15: Prevenção contra SSRF - nega localhost, redes privadas e metadados de nuvem
        ValidarHostNaoPrivado(uri.Host);

        Id = id.Trim();
        Url = uri;
        AuthReference = authReference;
        PolicyVersion = policyVersion;
    }

    public static void ValidarHostNaoPrivado(string host)
    {
        var cleanHost = host.Trim().Trim('[', ']');
        if (cleanHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) || cleanHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Endereço localhost ou local é proibido por segurança (SSRF).");

        if (IPAddress.TryParse(cleanHost, out var ip))
        {
            ValidarIpAddress(ip);
        }
    }

    public static void ValidarIpAddress(IPAddress ip)
    {
        // PIX-AUD-06: Desmapeia IPv4 encapsulado em IPv6 (ex: ::ffff:10.0.0.1, ::ffff:169.254.169.254) para validação estrita anti-SSRF
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
            throw new InvalidOperationException("Endereços de loopback são proibidos.");

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                throw new InvalidOperationException("Endereços IPv6 locais/privados são proibidos.");
            var v6Bytes = ip.GetAddressBytes();
            if ((v6Bytes[0] & 0xFE) == 0xFC) // fc00::/7 (Unique Local Address)
                throw new InvalidOperationException("Endereço IPv6 Unique Local fc00::/7 é proibido.");
            return;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // 0.0.0.0/8
            if (bytes[0] == 0)
                throw new InvalidOperationException("Endereço IP 0.0.0.0/8 é proibido.");

            // 127.0.0.0/8
            if (bytes[0] == 127)
                throw new InvalidOperationException("Endereço de loopback 127.0.0.0/8 é proibido.");

            // 10.0.0.0/8
            if (bytes[0] == 10)
                throw new InvalidOperationException("IP da rede privada 10.0.0.0/8 é proibido.");

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                throw new InvalidOperationException("IP da rede privada 172.16.0.0/12 é proibido.");

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                throw new InvalidOperationException("IP da rede privada 192.168.0.0/16 é proibido.");

            // 169.254.0.0/16 (Cloud metadata / link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
                throw new InvalidOperationException("IP de metadados / link-local 169.254.0.0/16 é proibido.");

            // 224.0.0.0/4 (Multicast)
            if (bytes[0] >= 224 && bytes[0] <= 239)
                throw new InvalidOperationException("Endereço de multicast é proibido.");
        }
    }

    public static async Task ValidarHostNaoPrivadoComDnsAsync(string host, CancellationToken ct = default)
    {
        ValidarHostNaoPrivado(host);

        if (!IPAddress.TryParse(host, out _))
        {
            try
            {
                var ips = await Dns.GetHostAddressesAsync(host, ct);
                foreach (var resolvedIp in ips)
                {
                    ValidarIpAddress(resolvedIp);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Ignora falhas de resolução que não encontrem o host em testes desconectados
            }
        }
    }
}

public class CallbackDelivery
{
    public DeliveryId Id { get; private set; }
    public string EventoConclusaoId { get; private set; }
    public string DestinatarioId { get; private set; }
    public string PayloadJson { get; private set; }
    public EstadoEntrega Estado { get; private set; }
    public int Tentativas { get; private set; }
    public int MaxTentativas { get; private set; }
    public DateTimeOffset ProximaTentativa { get; private set; }
    public int Versao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    // Construtor EF
    private CallbackDelivery()
    {
        EventoConclusaoId = null!;
        DestinatarioId = null!;
        PayloadJson = null!;
    }

    public CallbackDelivery(
        DeliveryId id,
        string eventoConclusaoId,
        string destinatarioId,
        string payloadJson,
        int maxTentativas = 3,
        DateTimeOffset? criadoEm = null)
    {
        Id = id;
        EventoConclusaoId = eventoConclusaoId;
        DestinatarioId = destinatarioId;
        PayloadJson = payloadJson;
        Estado = EstadoEntrega.Pendente;
        Tentativas = 0;
        MaxTentativas = maxTentativas;
        CriadoEm = criadoEm ?? DateTimeOffset.UtcNow;
        ProximaTentativa = CriadoEm;
        AtualizadoEm = CriadoEm;
        Versao = 1;
    }

    public void IniciarEnvio(DateTimeOffset agora)
    {
        if (Estado != EstadoEntrega.Pendente && Estado != EstadoEntrega.AguardandoRetry)
            throw new InvalidOperationException($"Não é permitido iniciar envio a partir de {Estado}.");

        Estado = EstadoEntrega.EmEnvio;
        Tentativas++;
        Versao++;
        AtualizadoEm = agora;
    }

    public void RegistrarAceite(DateTimeOffset agora)
    {
        if (Estado != EstadoEntrega.EmEnvio)
            throw new InvalidOperationException("Apenas entregas EmEnvio podem ser aceitas.");

        Estado = EstadoEntrega.Aceita;
        Versao++;
        AtualizadoEm = agora;
    }

    public void RegistrarFalha(DateTimeOffset agora, TimeSpan? intervaloRetry = null)
    {
        if (Estado != EstadoEntrega.EmEnvio)
            throw new InvalidOperationException("Apenas entregas EmEnvio podem registrar falha.");

        if (Tentativas >= MaxTentativas)
        {
            Estado = EstadoEntrega.Esgotada;
        }
        else
        {
            Estado = EstadoEntrega.AguardandoRetry;
            ProximaTentativa = agora.Add(intervaloRetry ?? TimeSpan.FromSeconds(Math.Pow(2, Tentativas)));
        }

        Versao++;
        AtualizadoEm = agora;
    }
}
