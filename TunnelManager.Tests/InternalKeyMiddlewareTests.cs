using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TunnelManager.ConfigApi.Middleware;
using TunnelManager.ConfigApi.Services;
using Xunit;

namespace TunnelManager.Tests;

public class InternalKeyMiddlewareTests
{
    private const string ValidKey = "test-secret-key-1234";

    private InternalKeyMiddleware CreateMiddleware(RequestDelegate next)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INTERNAL_CONFIG_API_KEY"] = ValidKey
            })
            .Build();

        var logger = new Mock<ILogger<InternalKeyMiddleware>>();
        return new InternalKeyMiddleware(next, logger.Object, config);
    }

    private DefaultHttpContext CreateContext(string? headerValue = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        // Provide a mock IAuditService in request services
        var services = new ServiceCollection();
        services.AddScoped<IAuditService>(_ =>
        {
            var mock = new Mock<IAuditService>();
            mock.Setup(s => s.LogAsync(It.IsAny<string>(), It.IsAny<string>(),
                                       It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            return mock.Object;
        });
        context.RequestServices = services.BuildServiceProvider();

        if (headerValue != null)
            context.Request.Headers["x-internal-key"] = headerValue;

        return context;
    }

    [Fact]
    public async Task Returns403_WhenHeaderMissing()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(); // no header
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(403, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Returns403_WhenKeyIsWrong()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("wrong-key");
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(403, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task CallsNext_WhenKeyIsCorrect()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext(ValidKey);
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }
}
