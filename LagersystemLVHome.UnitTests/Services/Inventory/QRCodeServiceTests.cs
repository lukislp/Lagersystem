namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class QRCodeServiceTests
{
    [Fact]
    public void GenerateQRCodeBytes_ReturnsPngMagicNumber()
    {
        var sut = new QRCodeService();

        var bytes = sut.GenerateQRCodeBytes("hello", size: 300);

        bytes.Should().NotBeEmpty();
        // PNG header: 89 50 4E 47 0D 0A 1A 0A
        bytes.Take(8).Should().BeEquivalentTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    [Fact]
    public void GenerateQRCode_ReturnsNonEmptyBase64()
    {
        var sut = new QRCodeService();

        var result = sut.GenerateQRCode("payload");

        result.Should().NotBeNullOrWhiteSpace();
        var action = () => Convert.FromBase64String(result);
        action.Should().NotThrow();
    }

    [Fact]
    public void GenerateQRCode_IsDeterministicForSameInput()
    {
        var sut = new QRCodeService();

        sut.GenerateQRCode("same", 300).Should().Be(sut.GenerateQRCode("same", 300));
    }

    [Fact]
    public void GenerateQRCode_ProducesDifferentOutputForDifferentContent()
    {
        var sut = new QRCodeService();

        sut.GenerateQRCode("a", 300).Should().NotBe(sut.GenerateQRCode("b", 300));
    }

    [Fact]
    public void GenerateStorageLocationQRCode_EncodesLocationCode()
    {
        var sut = new QRCodeService();

        var viaHelper = sut.GenerateStorageLocationQRCode(locationId: 42, locationCode: "A-1-2", size: 300);
        var direct = sut.GenerateQRCode("A-1-2", 300);

        viaHelper.Should().Be(direct);
    }
}
