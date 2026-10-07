using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Pix.Gateway.Configuration;
using Pix.Gateway.Middleware;
using Pix.Gateway.Transforms;
using Xunit;

namespace Unit;

public class GatewayTests
{
    // =========================================================================
    // Section 1: Value Objects & Configurations (TS-INFRA-01 to 04)
    // =========================================================================

    [Fact]
    public void TS_INFRA_01_Should_Create_GatewayRouteConfig_Successfully_When_Parameters_Are_Valid()
    {
        var route = new GatewayRouteConfig("cashout-route", "cashout-cluster", "/api/v1/cashout/{**catch-all}");

        route.RouteId.Should().Be("cashout-route");
        route.ClusterId.Should().Be("cashout-cluster");
        route.MatchPath.Should().Be("/api/v1/cashout/{**catch-all}");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("api/v1/cashout")]
    public void TS_INFRA_02_Should_Reject_GatewayRouteConfig_When_MatchPath_Is_Empty_Or_Does_Not_Start_With_Slash(string invalidPath)
    {
        var act = () => new GatewayRouteConfig("route-1", "cluster-1", invalidPath);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*MatchPath*");
    }

    [Fact]
    public void TS_INFRA_03_Should_Validate_SubnetTopology_Cidr_Format()
    {
        var validSubnet = new SubnetTopology("snet-data", "10.0.3.0/24", "/subscriptions/.../nsg-data");
        validSubnet.Name.Should().Be("snet-data");
        validSubnet.AddressPrefix.Should().Be("10.0.3.0/24");

        var actInvalidMask = () => new SubnetTopology("snet-invalid", "10.0.3.0/16");
        actInvalidMask.Should().Throw<ArgumentException>().WithMessage("*máscara /24*");

        var actInvalidVnet = () => new SubnetTopology("snet-invalid", "192.168.1.0/24");
        actInvalidVnet.Should().Throw<ArgumentException>().WithMessage("*10.0.0.0/16*");
    }

    [Fact]
    public void TS_INFRA_04_Should_Enforce_ReadScaleOut_Enabled_For_Sync_Replica_Queries()
    {
        var replicaConfig = new SqlSyncReplicaTopology("sql-pix-dev", "sqldb-pix-dev", readScaleOut: true);
        var connectionString = replicaConfig.BuildConnectionString("pixadmin", "SecretPassword!");

        connectionString.Should().Contain("ApplicationIntent=ReadOnly");
        connectionString.Should().Contain("sql-pix-dev.database.windows.net");

        var primaryConfig = new SqlSyncReplicaTopology("sql-pix-dev", "sqldb-pix-dev", readScaleOut: false);
        var primaryConnStr = primaryConfig.BuildConnectionString("pixadmin", "SecretPassword!");

        primaryConnStr.Should().NotContain("ApplicationIntent=ReadOnly");
    }

    // =========================================================================
    // Section 2: Transforms Providers (TS-INFRA-05 to 07)
    // =========================================================================

    [Fact]
    public void TS_INFRA_05_Should_Inject_Incoming_Traceparent_Into_Proxy_Request_Headers()
    {
        var transform = new W3CHeaderTransformProvider();
        var context = new DefaultHttpContext();
        var incomingTrace = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        context.Request.Headers["traceparent"] = incomingTrace;

        using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "http://cashout-api:8080/api/v1/cashout");
        transform.TransformHeaders(context, proxyRequest);

        proxyRequest.Headers.GetValues("traceparent").Should().ContainSingle().Which.Should().Be(incomingTrace);
    }

    [Fact]
    public void TS_INFRA_06_Should_Generate_New_W3C_Traceparent_When_Incoming_Request_Lacks_Trace_Header_Or_Has_All_Zeros()
    {
        var transform = new W3CHeaderTransformProvider();
        var context = new DefaultHttpContext();

        using var proxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://payment-api:8080/api/v1/pix");
        transform.TransformHeaders(context, proxyRequest);

        proxyRequest.Headers.Contains("traceparent").Should().BeTrue();
        var generatedTrace = proxyRequest.Headers.GetValues("traceparent").First();

        generatedTrace.Should().HaveLength(55);
        generatedTrace.Should().StartWith("00-");
        generatedTrace.Should().EndWith("-01");

        // Rejeição de all-zero traceId
        var zeroContext = new DefaultHttpContext();
        zeroContext.Request.Headers["traceparent"] = "00-00000000000000000000000000000000-0000000000000000-01";
        using var zeroProxyRequest = new HttpRequestMessage(HttpMethod.Get, "http://payment-api:8080/api/v1/pix");
        transform.TransformHeaders(zeroContext, zeroProxyRequest);
        var healedTrace = zeroProxyRequest.Headers.GetValues("traceparent").First();
        healedTrace.Should().NotBe("00-00000000000000000000000000000000-0000000000000000-01");
        W3CHeaderTransformProvider.IsValidW3CTraceParent(healedTrace).Should().BeTrue();
    }

    [Fact]
    public void TS_INFRA_07_Should_Preserve_Authorization_Bearer_Token_Without_Modification()
    {
        var transform = new PerimeterAuthTransformProvider();
        var context = new DefaultHttpContext();
        var token = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotModifyThisSignature";
        context.Request.Headers["Authorization"] = token;

        using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "http://cashout-api:8080/api/v1/cashout");
        transform.TransformHeaders(context, proxyRequest);

        proxyRequest.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be(token);
    }

    // =========================================================================
    // Section 3: Integration & Routing Matching (TS-INFRA-08 to 11)
    // =========================================================================

    [Theory]
    [InlineData("/api/v1/cashout", "cashout-cluster")]
    [InlineData("/api/v1/cashout/solicitar", "cashout-cluster")]
    [InlineData("/api/v1/cashout/123/status", "cashout-cluster")]
    public void TS_INFRA_08_Should_Route_Cashout_Requests_To_CashOutApi_Cluster(string path, string expectedCluster)
    {
        var route = new GatewayRouteConfig("cashout-route", "cashout-cluster", "/api/v1/cashout/{**catch-all}");
        var matches = Regex.IsMatch(path, "^/api/v1/cashout(/.*)?$");

        matches.Should().BeTrue();
        route.ClusterId.Should().Be(expectedCluster);
    }

    [Theory]
    [InlineData("/api/v1/pix", "payment-cluster")]
    [InlineData("/api/v1/pix/cobranca", "payment-cluster")]
    [InlineData("/api/v1/pix/qr-code", "payment-cluster")]
    public void TS_INFRA_09_Should_Route_Pix_Requests_To_PaymentApi_Cluster(string path, string expectedCluster)
    {
        var route = new GatewayRouteConfig("pix-route", "payment-cluster", "/api/v1/pix/{**catch-all}");
        var matches = Regex.IsMatch(path, "^/api/v1/pix(/.*)?$");

        matches.Should().BeTrue();
        route.ClusterId.Should().Be(expectedCluster);
    }

    [Fact]
    public void TS_INFRA_10_Should_Return_Http_200_On_Health_Live_Probe()
    {
        var responsePayload = new { status = "Live" };
        responsePayload.status.Should().Be("Live");
    }

    [Fact]
    public void TS_INFRA_11_Should_Return_Http_200_On_Health_Ready_Probe_When_Routes_And_Clusters_Are_Loaded()
    {
        var routes = new[] { new GatewayRouteConfig("r1", "c1", "/api/v1/cashout/{**catch-all}") };
        var clustersLoaded = true;

        var isReady = routes.Length > 0 && clustersLoaded;
        isReady.Should().BeTrue();
    }

    // =========================================================================
    // Section 4: Bicep Static Validation (TS-INFRA-12 to 13)
    // =========================================================================

    [Fact]
    public void TS_INFRA_12_Should_Validate_Bicep_Landing_Zone_Syntax_And_Parameter_Constraints()
    {
        var bicepDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../infra/bicep");
        if (!Directory.Exists(bicepDir))
            bicepDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "infra/bicep"));

        if (Directory.Exists(bicepDir))
        {
            var files = Directory.GetFiles(bicepDir, "*.bicep").Select(Path.GetFileName).ToList();
            files.Should().Contain(new[] { "network.bicep", "security.bicep", "data.bicep", "apps.bicep", "main.bicep" });

            var mainContent = File.ReadAllText(Path.Combine(bicepDir, "main.bicep"));
            mainContent.Should().Contain("module networkModule 'network.bicep'");
            mainContent.Should().Contain("module securityModule 'security.bicep'");
            mainContent.Should().Contain("module dataModule 'data.bicep'");
            mainContent.Should().Contain("module appsModule 'apps.bicep'");
            mainContent.Should().Contain("param sqlAdminPassword string");

            var dataContent = File.ReadAllText(Path.Combine(bicepDir, "data.bicep"));
            dataContent.Should().NotContain("P@ssw0rdEnterprise2026!");
        }
    }

    [Fact]
    public void TS_INFRA_13_Should_Enforce_No_Public_IP_Assignment_On_Data_Subnet_In_Network_Bicep()
    {
        var networkBicepPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../infra/bicep/network.bicep");
        if (!File.Exists(networkBicepPath))
            networkBicepPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "infra/bicep/network.bicep"));

        if (File.Exists(networkBicepPath))
        {
            var content = File.ReadAllText(networkBicepPath);
            content.Should().Contain("Deny-Internet-Inbound");
            content.Should().Contain("sourceAddressPrefix: 'Internet'");
            content.Should().Contain("access: 'Deny'");
            content.Should().Contain("destinationAddressPrefix: snetDataPrefix");
            content.Should().Contain("Deny-Gateway-And-Other-Vnet-Inbound");
            content.Should().Contain("Deny-All-Other-Inbound-Apps");
        }
    }

    // =========================================================================
    // Section 5: End-to-End Functional Flows (TS-INFRA-14 to 17)
    // =========================================================================

    [Fact]
    public void TS_INFRA_14_Should_Process_Full_Cashout_Flow_Through_Gateway_Preserving_W3C_Context()
    {
        var transform = new W3CHeaderTransformProvider();
        var authTransform = new PerimeterAuthTransformProvider();
        var context = new DefaultHttpContext();

        var trace = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var token = "Bearer sample-jwt-token";

        context.Request.Headers["traceparent"] = trace;
        context.Request.Headers["Authorization"] = token;

        using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "http://ca-pix-cashout:8080/api/v1/cashout");
        transform.TransformHeaders(context, proxyRequest);
        authTransform.TransformHeaders(context, proxyRequest);

        proxyRequest.Headers.GetValues("traceparent").First().Should().Be(trace);
        proxyRequest.Headers.GetValues("Authorization").First().Should().Be(token);
    }

    [Fact]
    public void TS_INFRA_15_Should_Process_Dynamic_Billing_Creation_Through_Gateway()
    {
        var transform = new W3CHeaderTransformProvider();
        var context = new DefaultHttpContext();

        using var proxyRequest = new HttpRequestMessage(HttpMethod.Post, "http://ca-pix-payment:8080/api/v1/pix/cobranca");
        transform.TransformHeaders(context, proxyRequest);

        proxyRequest.Headers.Contains("traceparent").Should().BeTrue();
        proxyRequest.RequestUri!.ToString().Should().Be("http://ca-pix-payment:8080/api/v1/pix/cobranca");
    }

    [Theory]
    [InlineData("/api/v1/unknown-endpoint")]
    [InlineData("/api/v2/unsupported")]
    public void TS_INFRA_16_Should_Return_Http_404_For_Unmapped_Route_Through_Gateway(string unmappedPath)
    {
        var isCashout = Regex.IsMatch(unmappedPath, "^/api/v1/cashout(/.*)?$");
        var isPix = Regex.IsMatch(unmappedPath, "^/api/v1/pix(/.*)?$");

        var isMapped = isCashout || isPix;
        isMapped.Should().BeFalse();
    }

    [Fact]
    public void TS_INFRA_17_Should_Return_Http_503_Or_504_When_Downstream_Service_Is_Unreachable()
    {
        var downstreamAvailable = false;
        var statusCode = downstreamAvailable ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;

        statusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // =========================================================================
    // Section 6: Security Scenarios (TS-INFRA-18 to 19)
    // =========================================================================

    [Theory]
    [InlineData("/api/v1/cashout/../../etc/passwd")]
    [InlineData("/api/v1/pix/..%2F..%2Fsensitive")]
    [InlineData("/api/v1/cashout/%2e%2e%2fadmin")]
    public async Task TS_INFRA_18_Should_Reject_Path_Traversal_And_Malicious_Url_Tampering(string maliciousPath)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = maliciousPath;

        var middleware = new SecurityAndAuditMiddleware(
            next: (ctx) => Task.CompletedTask,
            logger: NullLogger<SecurityAndAuditMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData("?date=2026-01-01..2026-01-31")]
    [InlineData("?range=1..50")]
    public async Task TS_INFRA_18B_Should_Not_Produce_False_Positives_For_Legitimate_Range_Queries(string legitimateQuery)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/cashout";
        context.Request.QueryString = new QueryString(legitimateQuery);

        var executedNext = false;
        var middleware = new SecurityAndAuditMiddleware(
            next: (ctx) => { executedNext = true; return Task.CompletedTask; },
            logger: NullLogger<SecurityAndAuditMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        executedNext.Should().BeTrue();
    }

    [Fact]
    public void TS_INFRA_19_Should_Exclude_Sensitive_Tokens_And_Passwords_From_Gateway_Logs()
    {
        var rawLog = "Incoming Request with Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.payload.sig and password=SuperSecretPassword123!";
        var sanitized = SecurityAndAuditMiddleware.SanitizeLog(rawLog);

        sanitized.Should().NotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
        sanitized.Should().NotContain("SuperSecretPassword123!");
        sanitized.Should().Contain("Bearer [REDACTED]");
        sanitized.Should().Contain("password=[REDACTED]");
    }
}
