namespace Playloud.Cliente.Bootstrap;

public sealed class ClienteStoragePaths
{
    private ClienteStoragePaths(
        string rootDirectory,
        string dataDirectory,
        string databasePath)
    {
        RootDirectory = rootDirectory;
        DataDirectory = dataDirectory;
        DatabasePath = databasePath;
    }

    public string RootDirectory { get; }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public static ClienteStoragePaths CreateDefault()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "Não foi possível localizar a pasta LocalApplicationData do Windows.");
        }

        return FromBaseDirectory(localApplicationData);
    }

    public static ClienteStoragePaths FromBaseDirectory(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var rootDirectory = Path.Combine(baseDirectory, "Playloud", "Cliente");
        var dataDirectory = Path.Combine(rootDirectory, "Data");
        var databasePath = Path.Combine(dataDirectory, "playloud-cliente.db");

        return new ClienteStoragePaths(rootDirectory, dataDirectory, databasePath);
    }

    public void EnsureDirectories() => Directory.CreateDirectory(DataDirectory);
}
