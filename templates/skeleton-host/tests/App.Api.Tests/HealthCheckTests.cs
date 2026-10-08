using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace App.Api.Tests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_WhenValidW3CTraceParentProvided_ShouldPropagateTrace()
    {
        // Arrange
        const string validTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("traceparent", validTraceParent);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("traceContextPropagated").GetBoolean().Should().BeTrue();
        root.GetProperty("traceContext").GetString().Should().Be(validTraceParent);
    }

    [Fact]
    public async Task GetHealth_WhenInvalidW3CTraceParentProvided_ShouldNotPropagateAndUseFallback()
    {
        // Arrange: Header fora do padrão de 55 caracteres e sem prefixo 00
        const string invalidTraceParent = "invalid-trace-header-123";
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("traceparent", invalidTraceParent);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("traceContextPropagated").GetBoolean().Should().BeFalse();
        root.GetProperty("traceContext").GetString().Should().NotBe(invalidTraceParent);
    }
}
