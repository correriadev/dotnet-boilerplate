using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Pix.Gateway.Transforms;

public class PerimeterAuthTransformProvider
{
    public const string AuthorizationHeader = "Authorization";

    public void ApplyAuthTransform(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            var authHeader = transformContext.HttpContext.Request.Headers[AuthorizationHeader].ToString();
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                transformContext.ProxyRequest.Headers.Remove(AuthorizationHeader);
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation(AuthorizationHeader, authHeader);
            }

            return ValueTask.CompletedTask;
        });
    }

    public void TransformHeaders(HttpContext context, HttpRequestMessage targetRequest)
    {
        var authHeader = context.Request.Headers[AuthorizationHeader].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            targetRequest.Headers.Remove(AuthorizationHeader);
            targetRequest.Headers.TryAddWithoutValidation(AuthorizationHeader, authHeader);
        }
    }
}
