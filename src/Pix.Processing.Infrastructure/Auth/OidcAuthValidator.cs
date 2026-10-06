using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pix.Processing.Infrastructure.Auth;

public class OidcAuthOptions
{
    public bool SimulatedMode { get; set; } = false;
    public string Authority { get; set; } = "https://auth.enterprise.internal/as";
    public string Audience { get; set; } = "pix-cashout-api";
    public string? SecretKey { get; set; }
}

public record AuthValidationResult(bool Autenticado, string? ClienteId, int StatusCode, string? Mensagem)
{
    public static AuthValidationResult Success(string clienteId) =>
        new(true, clienteId, 200, "Autenticado com sucesso");

    public static AuthValidationResult Unauthorized(string mensagem) =>
        new(false, null, 401, mensagem);
}

public interface IOidcAuthValidator
{
    Task<AuthValidationResult> ValidarTokenAsync(string? authHeader, CancellationToken ct = default);
}

public class OidcAuthValidator : IOidcAuthValidator
{
    private readonly OidcAuthOptions _options;

    public OidcAuthValidator(OidcAuthOptions? options = null)
    {
        _options = options ?? new OidcAuthOptions();
    }

    public Task<AuthValidationResult> ValidarTokenAsync(string? authHeader, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return Task.FromResult(AuthValidationResult.Unauthorized("Cabeçalho Authorization ausente"));
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthValidationResult.Unauthorized("Formato de token inválido, esperado Bearer"));
        }

        var token = authHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthValidationResult.Unauthorized("Token vazio"));
        }

        if (_options.SimulatedMode)
        {
            if (token.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthValidationResult.Unauthorized("Token simulado inválido ou expirado"));
            }

            // Em modo simulado (Dev / CI), aceita tokens simulados válidos sem exigir rede corporativa
            if (token.StartsWith("sim-jwt", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("valid-token", StringComparison.OrdinalIgnoreCase) ||
                token.Length > 20)
            {
                return Task.FromResult(AuthValidationResult.Success("cli-simulado-01"));
            }

            return Task.FromResult(AuthValidationResult.Unauthorized("Token simulado inválido ou expirado"));
        }

        // Modo Produção: Validação criptográfica do Token JWT (RFC 7519)
        return Task.FromResult(ValidarJwtCriptografico(token));
    }

    private AuthValidationResult ValidarJwtCriptografico(string token)
    {
        if (_options.SimulatedMode && (token.StartsWith("sim-jwt", StringComparison.OrdinalIgnoreCase) || token.Contains("valid-token", StringComparison.OrdinalIgnoreCase)))
        {
            return AuthValidationResult.Success("cli-simulado-01");
        }

        var partes = token.Split('.');
        if (partes.Length != 3)
        {
            return AuthValidationResult.Unauthorized("Estrutura JWT inválida, esperado header.payload.signature");
        }

        var headerBase64 = partes[0];
        var payloadBase64 = partes[1];
        var signatureBase64 = partes[2];

        // 1. Validação Criptográfica de Assinatura se SecretKey estiver configurada
        if (!_options.SimulatedMode)
        {
            if (string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                return AuthValidationResult.Unauthorized("Chave secreta (SecretKey) obrigatória para validação da assinatura HMAC em produção.");
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SecretKey));
            var rawData = Encoding.UTF8.GetBytes($"{headerBase64}.{payloadBase64}");
            var computedSignature = hmac.ComputeHash(rawData);
            var computedSignatureBase64 = Convert.ToBase64String(computedSignature).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedSignatureBase64),
                    Encoding.UTF8.GetBytes(signatureBase64)))
            {
                return AuthValidationResult.Unauthorized("Falha na validação da assinatura criptográfica do token JWT");
            }
        }
        else if (!string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SecretKey));
            var rawData = Encoding.UTF8.GetBytes($"{headerBase64}.{payloadBase64}");
            var computedSignature = hmac.ComputeHash(rawData);
            var computedSignatureBase64 = Convert.ToBase64String(computedSignature).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedSignatureBase64),
                    Encoding.UTF8.GetBytes(signatureBase64)))
            {
                return AuthValidationResult.Unauthorized("Falha na validação da assinatura criptográfica do token JWT");
            }
        }

        // 2. Decodificação do Payload
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payloadBase64));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            // Validação de Expiração (exp)
            if (root.TryGetProperty("exp", out var expProp))
            {
                var expSeconds = expProp.GetInt64();
                var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (nowSeconds > expSeconds)
                {
                    return AuthValidationResult.Unauthorized("Token JWT expirado");
                }
            }

            // Validação de Issuer (iss)
            if (root.TryGetProperty("iss", out var issProp))
            {
                var iss = issProp.GetString();
                if (!string.Equals(iss, _options.Authority, StringComparison.OrdinalIgnoreCase))
                {
                    return AuthValidationResult.Unauthorized($"Issuer inválido: {iss}");
                }
            }

            // Validação de Audience (aud)
            if (root.TryGetProperty("aud", out var audProp))
            {
                var aud = audProp.GetString();
                if (!string.Equals(aud, _options.Audience, StringComparison.OrdinalIgnoreCase))
                {
                    return AuthValidationResult.Unauthorized($"Audience inválido: {aud}");
                }
            }

            string clientId = "cli-enterprise-producao";
            if (root.TryGetProperty("sub", out var subProp) && !string.IsNullOrWhiteSpace(subProp.GetString()))
            {
                clientId = subProp.GetString()!;
            }
            else if (root.TryGetProperty("client_id", out var clientProp) && !string.IsNullOrWhiteSpace(clientProp.GetString()))
            {
                clientId = clientProp.GetString()!;
            }

            return AuthValidationResult.Success(clientId);
        }
        catch (Exception ex)
        {
            return AuthValidationResult.Unauthorized($"Falha ao decodificar claims do JWT: {ex.Message}");
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }
}

