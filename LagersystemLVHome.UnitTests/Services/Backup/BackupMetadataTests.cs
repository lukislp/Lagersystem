using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.Backup;

public class BackupMetadataTests
{
    [Fact]
    public void Defaults_AreSafe()
    {
        var meta = new BackupMetadata();

        meta.DatabaseProvider.Should().BeEmpty();
        meta.ApplicationVersion.Should().BeEmpty();
        meta.Version.Should().BeEmpty();
        meta.BackupType.Should().BeEmpty();
        meta.TableCounts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void RoundtripsThroughJson()
    {
        var src = new BackupMetadata
        {
            BackupDate = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            DatabaseProvider = "PostgreSQL",
            ApplicationVersion = "1.2.3",
            Version = "1.1",
            BackupType = "JSON",
            TableCounts = new Dictionary<string, int>
            {
                ["Users"] = 4,
                ["Products"] = 128,
                ["AuditLogs"] = 9001
            }
        };

        var json = JsonSerializer.Serialize(src);
        var dst = JsonSerializer.Deserialize<BackupMetadata>(json)!;

        dst.DatabaseProvider.Should().Be(src.DatabaseProvider);
        dst.ApplicationVersion.Should().Be(src.ApplicationVersion);
        dst.Version.Should().Be(src.Version);
        dst.BackupType.Should().Be(src.BackupType);
        dst.TableCounts.Should().BeEquivalentTo(src.TableCounts);
        dst.BackupDate.Should().Be(src.BackupDate);
    }

    [Fact]
    public void TableCounts_TotalRecordsAggregation()
    {
        var meta = new BackupMetadata
        {
            TableCounts = new Dictionary<string, int>
            {
                ["A"] = 10,
                ["B"] = 20,
                ["C"] = 5
            }
        };

        meta.TableCounts.Values.Sum().Should().Be(35);
        meta.TableCounts.Count.Should().Be(3);
    }
}

public class BackupProviderConfigsTests
{
    [Fact]
    public void LocalBackupConfig_DefaultsAreSafe()
    {
        var c = new LocalBackupConfig();
        c.Paths.Should().ContainSingle();
        c.MaxBackups.Should().Be(7);
        c.CreateDateSubfolders.Should().BeFalse();
    }

    [Fact]
    public void AzureBlobConfig_HasReasonableDefaults()
    {
        var c = new AzureBlobConfig();
        c.ContainerName.Should().Be("lagersystem-backups");
        c.MaxBackups.Should().Be(30);
        c.ConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void AwsS3Config_DefaultRegionIsEuCentral1()
    {
        new AwsS3Config().Region.Should().Be("eu-central-1");
        new AwsS3Config().MaxBackups.Should().Be(30);
    }

    [Fact]
    public void NetworkShareConfig_StartsEmpty()
    {
        var c = new NetworkShareConfig();
        c.Paths.Should().BeEmpty();
        c.Username.Should().BeNull();
        c.Password.Should().BeNull();
        c.MaxBackups.Should().Be(30);
    }

    [Fact]
    public void FtpConfig_DefaultsToPort21NoSsl()
    {
        var c = new FtpConfig();
        c.Port.Should().Be(21);
        c.UseSsl.Should().BeFalse();
        c.RemotePath.Should().Be("/backups");
    }
}
