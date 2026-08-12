using System.Text;
using System.Text.Json;
using LagersystemLVHome.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Infrastructure;

public class GlobalExceptionHandlerTests
{
    private static IHostEnvironment CreateEnvironment(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static HttpContext CreateHttpContext(string path = "/", string accept = "text/html")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "GET";
        ctx.Request.Headers.Accept = accept;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsFalse_ForOperationCanceledException()
    {
        var sut = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, CreateEnvironment("Production"));
        var ctx = CreateHttpContext("/api/x", "application/json");

        var handled = await sut.TryHandleAsync(ctx, new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsFalse_ForNonApiRequest()
    {
        var sut = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, CreateEnvironment("Production"));
        var ctx = CreateHttpContext("/home", "text/html");

        var handled = await sut.TryHandleAsync(ctx, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeFalse();
        ReadBody(ctx).Should().BeEmpty();
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsTrueAndWritesProblemDetails_ForApiPath()
    {
        var sut = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, CreateEnvironment("Production"));
        var ctx = CreateHttpContext("/api/products", "text/html");

        var handled = await sut.TryHandleAsync(ctx, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var body = ReadBody(ctx);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        doc.RootElement.TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsTrue_ForJsonAcceptHeader()
    {
        var sut = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, CreateEnvironment("Production"));
        var ctx = CreateHttpContext("/home", "application/json");

        var handled = await sut.TryHandleAsync(ctx, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_IncludesDetail_InDevelopment()
    {
        var sut = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, CreateEnvironment(Environments.Development));
        var ctx = CreateHttpContext("/api/x", "application/json");

        var handled = await sut.TryHandleAsync(ctx, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeTrue();
        var body = ReadBody(ctx);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("detail").GetString().Should().Contain("boom");
    }
}
