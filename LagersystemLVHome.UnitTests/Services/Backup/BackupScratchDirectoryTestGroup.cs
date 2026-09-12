namespace LagersystemLVHome.UnitTests.Services.Backup;

/// <summary>
/// Serialises the test classes that drive the real JsonBackupHelper / restore services: they
/// create scratch directories under Path.GetTempPath() and one of them identifies its own
/// directory as the newest one, which is only unambiguous while these classes do not run in
/// parallel with each other (xUnit parallelises across classes by default).
/// </summary>
[CollectionDefinition(Name)]
public sealed class BackupScratchDirectoryTestGroup
{
    public const string Name = "Backup scratch directory";
}
