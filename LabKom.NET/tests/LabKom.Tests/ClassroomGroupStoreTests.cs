using LabKom.Teacher.Services;

namespace LabKom.Tests;

public sealed class ClassroomGroupStoreTests
{
    [Fact]
    public void GroupStoreNormalizesAndPersistsMembers()
    {
        var (root, path) = CreateStoragePath();
        try
        {
            var store = new ClassroomGroupStore(path);
            var saved = store.Upsert(
                "  Kelas Depan  ",
                new[] { "PC-02", "pc-01", "PC-02" });

            Assert.Equal("Kelas Depan", saved.Name);
            Assert.Equal(2, saved.PcNames.Count);

            var reloaded = new ClassroomGroupStore(path).Snapshot();
            var group = Assert.Single(reloaded);
            Assert.Equal(saved.Name, group.Name);
            Assert.Equal(saved.PcNames, group.PcNames);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void GroupStoreRejectsInvalidMembersAndPersistsDelete()
    {
        var (root, path) = CreateStoragePath();
        try
        {
            var store = new ClassroomGroupStore(path);

            Assert.Throws<ArgumentException>(() =>
                store.Upsert("Kosong", Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() =>
                store.Upsert("Rusak", new[] { "PC/name" }));

            store.Upsert("Sementara", new[] { "PC-01" });
            Assert.True(store.Delete("sementara"));
            Assert.Empty(new ClassroomGroupStore(path).Snapshot());
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void FailedSaveDoesNotMutateInMemoryGroups()
    {
        var (root, _) = CreateStoragePath();
        var blockingFile = Path.Combine(root, "not-a-folder");
        File.WriteAllText(blockingFile, "blocked");
        var store = new ClassroomGroupStore(
            Path.Combine(blockingFile, "groups.json"));

        try
        {
            Assert.ThrowsAny<IOException>(() =>
                store.Upsert("Tidak Tersimpan", new[] { "PC-01" }));
            Assert.Empty(store.Snapshot());
        }
        finally
        {
            SafeDelete(root);
        }
    }
    private static (string Root, string Path) CreateStoragePath()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "LabKom.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (root, Path.Combine(root, "groups.json"));
    }

    private static void SafeDelete(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (fullRoot.StartsWith(
                tempRoot,
                StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }
}