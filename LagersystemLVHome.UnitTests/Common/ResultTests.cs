using LagersystemLVHome;

namespace LagersystemLVHome.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Success_ProducesSuccessfulResult()
    {
        var r = Result<int>.Success(42);

        r.IsSuccess.Should().BeTrue();
        r.IsFailure.Should().BeFalse();
        r.Value.Should().Be(42);
        r.ErrorCode.Should().BeNull();
        r.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failure_ProducesFailedResultWithCodeAndMessage()
    {
        var r = Result<int>.Failure("err.code", "because");

        r.IsFailure.Should().BeTrue();
        r.IsSuccess.Should().BeFalse();
        r.ErrorCode.Should().Be("err.code");
        r.ErrorMessage.Should().Be("because");
    }

    [Fact]
    public void ValueOr_ReturnsValueOnSuccessAndFallbackOnFailure()
    {
        Result<int>.Success(7).ValueOr(99).Should().Be(7);
        Result<int>.Failure("x").ValueOr(99).Should().Be(99);
    }

    [Fact]
    public void Map_TransformsOnSuccessAndPropagatesFailure()
    {
        Result<int>.Success(3).Map(i => i * 2).Value.Should().Be(6);

        var failed = Result<int>.Failure("boom", "nope").Map(i => i * 2);
        failed.IsFailure.Should().BeTrue();
        failed.ErrorCode.Should().Be("boom");
        failed.ErrorMessage.Should().Be("nope");
    }
}
