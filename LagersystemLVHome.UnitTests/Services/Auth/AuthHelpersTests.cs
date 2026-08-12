using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class AuthHelpersTests
{
    // AuthHelpers is `internal static` -> access via reflection to keep production
    // surface unchanged (otherwise we'd have to add InternalsVisibleTo).
    private static readonly Type HelpersType = typeof(LagersystemLVHome.Application.Services.UtcDateTimeConverter)
        .Assembly
        .GetType("LagersystemLVHome.Application.Services.AuthHelpers", throwOnError: true)!;

    private static string? InvokeGetClientIp(IHttpContextAccessor? accessor)
    {
        var method = HelpersType.GetMethod("GetClientIp", BindingFlags.Public | BindingFlags.Static)!;
        return (string?)method.Invoke(null, new object?[] { accessor });
    }

    private static Task InvokeSafeLogAsync(IAuditService? audit, ILogger logger, string action)
    {
        var method = HelpersType.GetMethod("SafeLogAsync", BindingFlags.Public | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, new object?[]
        {
            audit, logger, action, "TestEntity", (int?)1, (object?)null,
            AuditSeverity.Info, CancellationToken.None
        })!;
        return task;
    }

    [Fact]
    public void GetClientIp_NoHttpContext_ReturnsNull()
    {
        InvokeGetClientIp(accessor: null).Should().BeNull();

        var emptyAccessor = Substitute.For<IHttpContextAccessor>();
        emptyAccessor.HttpContext.Returns((HttpContext?)null);
        InvokeGetClientIp(emptyAccessor).Should().BeNull();
    }

    [Fact]
    public void GetClientIp_PrefersForwardedForFirstHop()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 70.41.3.18, 150.172.238.178";
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);

        InvokeGetClientIp(accessor).Should().Be("203.0.113.5");
    }

    [Fact]
    public void GetClientIp_FallsBackToRemoteIpAddress()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);

        InvokeGetClientIp(accessor).Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task SafeLogAsync_NullAudit_DoesNotThrow()
    {
        var act = () => InvokeSafeLogAsync(audit: null, NullLogger.Instance, "X");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SafeLogAsync_DelegatesToAuditService()
    {
        var audit = Substitute.For<IAuditService>();

        await InvokeSafeLogAsync(audit, NullLogger.Instance, "MY_ACTION");

        await audit.Received(1).LogAsync(
            "MY_ACTION", "TestEntity", 1, Arg.Any<object?>(), AuditSeverity.Info);
    }

    [Fact]
    public async Task SafeLogAsync_AuditFailure_IsSwallowed()
    {
        var audit = Substitute.For<IAuditService>();
        audit
            .When(a => a.LogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<object?>(), Arg.Any<AuditSeverity>()))
            .Do(_ => throw new InvalidOperationException("audit DB down"));

        var act = () => InvokeSafeLogAsync(audit, NullLogger.Instance, "X");

        await act.Should().NotThrowAsync(because: "audit failures must never break business operations");
    }
}
