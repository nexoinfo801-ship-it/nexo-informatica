using Playloud.Cliente.Bootstrap;

namespace Playloud.Cliente.Tests;

public sealed class ClienteStoragePathsTests
{
    [Fact]
    public void FromBaseDirectory_builds_stable_cliente_data_paths()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"playloud-paths-{Guid.NewGuid():N}");

        var paths = ClienteStoragePaths.FromBaseDirectory(baseDirectory);

        Assert.Equal(
            Path.Combine(baseDirectory, "Playloud", "Cliente"),
            paths.RootDirectory);
        Assert.Equal(
            Path.Combine(baseDirectory, "Playloud", "Cliente", "Data"),
            paths.DataDirectory);
        Assert.Equal(
            Path.Combine(baseDirectory, "Playloud", "Cliente", "Data", "playloud-cliente.db"),
            paths.DatabasePath);
    }

    [Fact]
    public void EnsureDirectories_creates_data_directory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"playloud-paths-{Guid.NewGuid():N}");

        try
        {
            var paths = ClienteStoragePaths.FromBaseDirectory(baseDirectory);

            paths.EnsureDirectories();

            Assert.True(Directory.Exists(paths.DataDirectory));
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }
}
