using Pix.Gateway.Middleware;
using Pix.Gateway.Transforms;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add Yarp Reverse Proxy
var w3cTransform = new W3CHeaderTransformProvider();
var authTransform = new PerimeterAuthTransformProvider();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        w3cTransform.ApplyW3CTransform(builderContext);
        authTransform.ApplyAuthTransform(builderContext);
    });

var app = builder.Build();

// Security and audit middleware
app.UseMiddleware<SecurityAndAuditMiddleware>();

// Health Probes
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

app.MapGet("/health/ready", (IProxyConfigProvider proxyConfigProvider) =>
{
    var config = proxyConfigProvider.GetConfig();
    var isReady = config.Routes.Count > 0 && config.Clusters.Count > 0;
    return isReady 
        ? Results.Ok(new { status = "Ready", routes = config.Routes.Count, clusters = config.Clusters.Count })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

// Map Reverse Proxy
app.MapReverseProxy();

app.Run();

public partial class Program { }
