using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pix.CashOut.Api.Controllers;
using Pix.Payment.Api.Controllers;
using Pix.Processing.Application.Features.AcceptRequest;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;
using Pix.Processing.Infrastructure.Auth;
using Xunit;

namespace Integration;

public class ApiControllerAuthenticationTests
{
    private (AcceptRequestUseCase useCase, IOidcAuthValidator authValidator) SetupDependencies(string dbName)
    {
        var options = new DbContextOptionsBuilder<PixDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new PixDbContext(options);

        var auth = new AutorizacaoAdapter();
        auth.RegistrarVinculo(new ClienteId("cli-1"), new ContaId("cnt-1"));
        auth.RegistrarVinculo(new ClienteId("cli-simulado-01"), new ContaId("cnt-1"));

        var store = new AcceptanceStore(db);
        var limiter = new RedisAdmissionLimiter(10, TimeSpan.FromSeconds(10), TestRedisHelper.CriarMockRedisLimiter());
        var useCase = new AcceptRequestUseCase(auth, store, limiter);

        var authValidator = new OidcAuthValidator(new OidcAuthOptions
        {
            SimulatedMode = true,
            Authority = "https://auth.enterprise.internal/as",
            Audience = "pix-cashout-api"
        });

        return (useCase, authValidator);
    }

    [Fact]
    public async Task Should_Rejeitar_CashOut_Sem_Authorization_Header_Com_401()
    {
        // Given
        var (useCase, authValidator) = SetupDependencies(Guid.NewGuid().ToString());
        var controller = new CashOutController(useCase, authValidator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var request = new CashOutRequestDto("cli-1", "cnt-1", "idemp-1", 100m, "BRL", "E2E-1");

        // When
        var result = await controller.SolicitarCashOut(request, CancellationToken.None);

        // Then
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Should_Rejeitar_CashOut_Com_Token_Invalido_Com_401()
    {
        // Given
        var (useCase, authValidator) = SetupDependencies(Guid.NewGuid().ToString());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer invalid-token";

        var controller = new CashOutController(useCase, authValidator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var request = new CashOutRequestDto("cli-1", "cnt-1", "idemp-2", 100m, "BRL", "E2E-2");

        // When
        var result = await controller.SolicitarCashOut(request, CancellationToken.None);

        // Then
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Should_Permitir_CashOut_Com_Token_Simulado_Valido_Com_202()
    {
        // Given
        var (useCase, authValidator) = SetupDependencies(Guid.NewGuid().ToString());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer sim-jwt-valid-token-for-ci";

        var controller = new CashOutController(useCase, authValidator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var request = new CashOutRequestDto("cli-1", "cnt-1", "idemp-3", 100m, "BRL", "E2E-3");

        // When
        var result = await controller.SolicitarCashOut(request, CancellationToken.None);

        // Then
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(202);
    }

    [Fact]
    public async Task Should_Rejeitar_Billing_Sem_Authorization_Header_Com_401()
    {
        // Given
        var (useCase, authValidator) = SetupDependencies(Guid.NewGuid().ToString());
        var controller = new BillingController(useCase, authValidator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var request = new CriarCobrancaRequestDto("cli-1", "cnt-1", "idemp-4", 150m, "BRL", "REF-1");

        // When
        var result = await controller.SolicitarCobranca(request, CancellationToken.None);

        // Then
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Should_Rejeitar_Billing_Com_Token_Invalido_Com_401()
    {
        // Given
        var (useCase, authValidator) = SetupDependencies(Guid.NewGuid().ToString());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer invalid-expired-token";

        var controller = new BillingController(useCase, authValidator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var request = new CriarCobrancaRequestDto("cli-1", "cnt-1", "idemp-5", 150m, "BRL", "REF-2");

        // When
        var result = await controller.SolicitarCobranca(request, CancellationToken.None);

        // Then
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Should_Permitir_Billing_Com_Token_Simulado_Valido_Com_202()
    {
        // Given
        var (useCase, authValidator) = SetupDependencies(Guid.NewGuid().ToString());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Authorization"] = "Bearer sim-jwt-valid-token-for-ci";

        var controller = new BillingController(useCase, authValidator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var request = new CriarCobrancaRequestDto("cli-1", "cnt-1", "idemp-6", 150m, "BRL", "REF-3");

        // When
        var result = await controller.SolicitarCobranca(request, CancellationToken.None);

        // Then
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(202);
    }
}
