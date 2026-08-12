using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class PwnedPasswordServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUris { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responder(request));
        }
    }

    private static (string prefix, string suffix) Sha1(string password)
    {
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpper();
        return (hash[..5], hash[5..]);
    }

    private static PwnedPasswordService BuildSut(FakeHandler handler)
    {
        var client = new HttpClient(handler);
        return new PwnedPasswordService(client, NullLogger<PwnedPasswordService>.Instance);
    }

    [Fact]
    public async Task CheckPasswordAsync_EmptyInput_ReturnsNotCompromised()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = BuildSut(handler);

        var result = await sut.CheckPasswordAsync("");

        result.IsCompromised.Should().BeFalse();
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPasswordAsync_HashFound_ReportsCompromised()
    {
        var (_, suffix) = Sha1("password123");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:1\n{suffix}:42\n")
        });
        var sut = BuildSut(handler);

        var result = await sut.CheckPasswordAsync("password123");

        result.IsCompromised.Should().BeTrue();
        result.BreachCount.Should().Be(42);
    }

    [Fact]
    public async Task CheckPasswordAsync_HighBreachCount_UsesCriticalMessage()
    {
        var (_, suffix) = Sha1("hunter2");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{suffix}:5000\n")
        });
        var sut = BuildSut(handler);

        var result = await sut.CheckPasswordAsync("hunter2");

        result.IsCompromised.Should().BeTrue();
        result.Message.Should().StartWith("KRITISCH");
    }

    [Fact]
    public async Task CheckPasswordAsync_HashNotInResponse_ReturnsSafe()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:1\n")
        });
        var sut = BuildSut(handler);

        var result = await sut.CheckPasswordAsync("very-secure-passphrase!");

        result.IsCompromised.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPasswordAsync_ApiError_FailsOpen()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = BuildSut(handler);

        var result = await sut.CheckPasswordAsync("anything");

        result.IsCompromised.Should().BeFalse();
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckPasswordAsync_HandlerThrows_FailsOpen()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("network down"));
        var sut = BuildSut(handler);

        var result = await sut.CheckPasswordAsync("anything");

        result.IsCompromised.Should().BeFalse();
    }

    [Fact]
    public async Task IsPasswordCompromisedAsync_DelegatesToCheck()
    {
        var (_, suffix) = Sha1("abc");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{suffix}:7\n")
        });
        var sut = BuildSut(handler);

        (await sut.IsPasswordCompromisedAsync("abc")).Should().BeTrue();
    }

    [Fact]
    public async Task CheckPasswordAsync_SendsKAnonymityPrefix()
    {
        var (prefix, _) = Sha1("p@ssword");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("")
        });
        var sut = BuildSut(handler);

        await sut.CheckPasswordAsync("p@ssword");

        handler.RequestedUris.Should().ContainSingle()
            .Which.Should().EndWith($"range/{prefix}");
    }
}
