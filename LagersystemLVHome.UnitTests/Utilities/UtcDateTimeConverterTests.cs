using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Utilities;

public class UtcDateTimeConverterTests
{
    private static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new UtcDateTimeConverter());
        opts.Converters.Add(new UtcNullableDateTimeConverter());
        return opts;
    }

    private sealed record Sample(DateTime Stamp, DateTime? Optional);

    [Fact]
    public void Roundtrip_UtcKind_PreservesValueAndKind()
    {
        var src = new Sample(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc), null);
        var opts = CreateOptions();

        var json = JsonSerializer.Serialize(src, opts);
        var dst = JsonSerializer.Deserialize<Sample>(json, opts)!;

        dst.Stamp.Kind.Should().Be(DateTimeKind.Utc);
        dst.Stamp.Should().Be(src.Stamp);
        dst.Optional.Should().BeNull();
    }

    [Fact]
    public void Write_LocalKind_NormalizesToUtc()
    {
        var local = new DateTime(2024, 6, 15, 14, 0, 0, DateTimeKind.Local);
        var opts = CreateOptions();

        var json = JsonSerializer.Serialize(new Sample(local, null), opts);
        var dst = JsonSerializer.Deserialize<Sample>(json, opts)!;

        dst.Stamp.Kind.Should().Be(DateTimeKind.Utc);
        dst.Stamp.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void Read_UnspecifiedKind_TreatedAsUtc()
    {
        var json = "{\"Stamp\":\"2024-06-15T12:00:00\",\"Optional\":null}";
        var opts = CreateOptions();

        var dst = JsonSerializer.Deserialize<Sample>(json, opts)!;

        dst.Stamp.Kind.Should().Be(DateTimeKind.Utc);
        dst.Stamp.Should().Be(new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NullableConverter_RoundtripsNull()
    {
        var opts = CreateOptions();

        var json = JsonSerializer.Serialize(new Sample(DateTime.UtcNow, null), opts);
        var dst = JsonSerializer.Deserialize<Sample>(json, opts)!;

        dst.Optional.Should().BeNull();
    }

    [Fact]
    public void NullableConverter_RoundtripsValue()
    {
        var stamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var opts = CreateOptions();

        var json = JsonSerializer.Serialize(new Sample(stamp, stamp), opts);
        var dst = JsonSerializer.Deserialize<Sample>(json, opts)!;

        dst.Optional.Should().NotBeNull();
        dst.Optional!.Value.Kind.Should().Be(DateTimeKind.Utc);
        dst.Optional.Value.Should().Be(stamp);
    }
}
