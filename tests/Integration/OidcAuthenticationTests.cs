using FluentAssertions;
using Pix.Processing.Infrastructure.Auth;
using Xunit;

namespace Integration;

public class OidcAuthenticationTests
{
    [Fact]
    public async Task AUTH_024_Should_Aceitar_Token_Jwt_Emitido_Por_Autoridade_Simulada_When_Auth_SimulatedMode_E_True()
    {
        // Given: Configuração com Auth:SimulatedMode = true
        var options = new OidcAuthOptions
        {
            SimulatedMode = true,
            Authority = "https://auth.enterprise.internal/as",
            Audience = "pix-cashout-api"
        };
        var validator = new OidcAuthValidator(options);

        // When: Token de teste simulado é apresentado
        var token = "Bearer sim-jwt-valid-token-for-ci";
        var resultado = await validator.ValidarTokenAsync(token);

        // Then: Autentica com sucesso sem depender da autoridade remota externa
        resultado.Autenticado.Should().BeTrue();
        resultado.ClienteId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AUTH_025_Should_Rejeitar_Requisicao_Sem_Token_When_Endpoint_Exige_Autenticacao()
    {
        // Given: Validador ativo
        var options = new OidcAuthOptions
        {
            SimulatedMode = true,
            Authority = "https://auth.enterprise.internal/as",
            Audience = "pix-cashout-api"
        };
        var validator = new OidcAuthValidator(options);

        // When 1: Requisição sem token (null ou vazio)
        var resNulo = await validator.ValidarTokenAsync(null);
        var resVazio = await validator.ValidarTokenAsync("   ");

        // Then 1: Rejeitar com 401 Unauthorized
        resNulo.Autenticado.Should().BeFalse();
        resNulo.StatusCode.Should().Be(401);

        resVazio.Autenticado.Should().BeFalse();
        resVazio.StatusCode.Should().Be(401);

        // When 2: Token inválido
        var resInvalido = await validator.ValidarTokenAsync("Bearer invalid-expired-token");
        resInvalido.Autenticado.Should().BeFalse();
        resInvalido.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task AUTH_RW_002_Should_Validar_Assinatura_Criptografica_JWT_E_Rejeitar_Token_Adulterado_Ou_Expirado()
    {
        // Given: Validador em modo de produção (SimulatedMode = false) com chave secreta corporativa
        const string secretKey = "auth-secret-key-enterprise-super-secure-256";
        const string authority = "https://auth.enterprise.internal/as";
        const string audience = "pix-cashout-api";

        var options = new OidcAuthOptions
        {
            SimulatedMode = false,
            Authority = authority,
            Audience = audience,
            SecretKey = secretKey
        };
        var validator = new OidcAuthValidator(options);

        // When 1: Token válido com assinatura criptográfica HMAC-SHA256
        var tokenValido = GerarJwt("cli-enterprise-prod-100", secretKey, authority, audience, DateTimeOffset.UtcNow.AddMinutes(15));
        var resValido = await validator.ValidarTokenAsync($"Bearer {tokenValido}");

        // Then 1: Autenticado com sucesso e claim do cliente extraído
        resValido.Autenticado.Should().BeTrue();
        resValido.ClienteId.Should().Be("cli-enterprise-prod-100");

        // When 2: Token com assinatura adulterada (tampered)
        var tokenAdulterado = GerarJwt("cli-enterprise-prod-100", secretKey, authority, audience, DateTimeOffset.UtcNow.AddMinutes(15), adulterarAssinatura: true);
        var resAdulterado = await validator.ValidarTokenAsync($"Bearer {tokenAdulterado}");

        // Then 2: Rejeitado por assinatura inválida
        resAdulterado.Autenticado.Should().BeFalse();
        resAdulterado.StatusCode.Should().Be(401);
        resAdulterado.Mensagem.Should().Contain("assinatura", "Token com assinatura alterada deve ser rejeitado");

        // When 3: Token expirado
        var tokenExpirado = GerarJwt("cli-enterprise-prod-100", secretKey, authority, audience, DateTimeOffset.UtcNow.AddMinutes(-5));
        var resExpirado = await validator.ValidarTokenAsync($"Bearer {tokenExpirado}");

        // Then 3: Rejeitado por expiração
        resExpirado.Autenticado.Should().BeFalse();
        resExpirado.StatusCode.Should().Be(401);
        resExpirado.Mensagem.Should().Contain("expirado");
    }

    [Fact]
    public async Task QA_001_Should_Rejeitar_Token_Sem_SecretKey_Configurada_When_SimulatedMode_E_False()
    {
        // Given: SimulatedMode = false sem SecretKey configurada
        var options = new OidcAuthOptions
        {
            SimulatedMode = false,
            Authority = "https://auth.enterprise.internal/as",
            Audience = "pix-cashout-api",
            SecretKey = null
        };
        var validator = new OidcAuthValidator(options);

        var tokenValido = GerarJwt("cli-prod", "temp-secret", options.Authority, options.Audience, DateTimeOffset.UtcNow.AddMinutes(10));

        // When: Apresenta token JWT
        var resultado = await validator.ValidarTokenAsync($"Bearer {tokenValido}");

        // Then: Deve rejeitar com 401 por falta da SecretKey obrigatória
        resultado.Autenticado.Should().BeFalse();
        resultado.StatusCode.Should().Be(401);
        resultado.Mensagem.Should().Contain("SecretKey");
    }

    [Fact]
    public async Task QA_002_Should_Aceitar_Token_Simulado_Prefixado_Com_SimJwt_When_SimulatedMode_E_True()
    {
        // Given: SimulatedMode = true
        var options = new OidcAuthOptions
        {
            SimulatedMode = true
        };
        var validator = new OidcAuthValidator(options);

        // When: Token com prefixo sim-jwt
        var resultado = await validator.ValidarTokenAsync("Bearer sim-jwt-custom-orchestrator-token");

        // Then: Aceito com sucesso
        resultado.Autenticado.Should().BeTrue();
        resultado.StatusCode.Should().Be(200);
        resultado.ClienteId.Should().Be("cli-simulado-01");
    }

    private static string GerarJwt(string clientId, string secret, string issuer, string audience, DateTimeOffset expiresAt, bool adulterarAssinatura = false)
    {
        var headerJson = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var payloadJson = $"{{\"sub\":\"{clientId}\",\"iss\":\"{issuer}\",\"aud\":\"{audience}\",\"exp\":{expiresAt.ToUnixTimeSeconds()}}}";

        var headerBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(headerJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payloadBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var unsignedToken = $"{headerBase64}.{payloadBase64}";

        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var signatureBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(unsignedToken));
        if (adulterarAssinatura)
        {
            signatureBytes[0] ^= 0xFF;
        }
        var signatureBase64 = Convert.ToBase64String(signatureBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{unsignedToken}.{signatureBase64}";
    }
}
