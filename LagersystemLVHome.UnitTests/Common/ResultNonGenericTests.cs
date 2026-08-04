using LagersystemLVHome;

namespace LagersystemLVHome.UnitTests.Common;

public class ResultNonGenericTests
{
    [Fact]
    public void Success_ProducesSuccessfulResult()
    {
        var r = Result.Success();

        r.IsSuccess.Should().BeTrue();
        r.IsFailure.Should().BeFalse();
        r.ErrorCode.Should().BeNull();
        r.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failure_WithCodeOnly_HasNullMessage()
    {
        var r = Result.Failure("some.code");

        r.IsFailure.Should().BeTrue();
        r.IsSuccess.Should().BeFalse();
        r.ErrorCode.Should().Be("some.code");
        r.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failure_WithCodeAndMessage_CarriesBoth()
    {
        var r = Result.Failure("some.code", "human detail");

        r.ErrorCode.Should().Be("some.code");
        r.ErrorMessage.Should().Be("human detail");
    }

    [Fact]
    public void Equals_ReturnsTrueForIdenticalValues()
    {
        Result.Success().Should().Be(Result.Success());
        Result.Failure("a", "b").Should().Be(Result.Failure("a", "b"));
        Result.Failure("a").Should().NotBe(Result.Failure("b"));
    }
}
