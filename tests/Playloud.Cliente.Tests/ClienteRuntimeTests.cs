using Playloud.Cliente.Bootstrap;

namespace Playloud.Cliente.Tests;

public sealed class ClienteRuntimeTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"playloud-cliente-runtime-{Guid.NewGuid():N}.db");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Create_initializes_database_and_checkout_runtime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var runtime = await ClienteRuntime.CreateAsync(
            _databasePath,
            cancellationToken);

        Assert.True(File.Exists(_databasePath));
        Assert.Equal("Pronto para vender.", runtime.Checkout.StatusMessage);
    }

    [Fact]
    public async Task Dispose_releases_database_for_cleanup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = await ClienteRuntime.CreateAsync(_databasePath, cancellationToken);

        await runtime.DisposeAsync();

        File.Delete(_databasePath);
        Assert.False(File.Exists(_databasePath));
    }
}
