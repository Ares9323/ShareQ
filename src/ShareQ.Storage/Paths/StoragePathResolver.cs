using System.Reflection;
using Microsoft.Extensions.Options;
using ShareQ.Storage.Options;

namespace ShareQ.Storage.Paths;

public sealed class StoragePathResolver : IStoragePathResolver
{
    private const string PortableMarkerFileName = "portable.txt";
    private const string AppDataFolderName = "ShareQ";

    private readonly StorageOptions _options;

    public StoragePathResolver(IOptions<StorageOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string ResolveRoot()
    {
        var root = _options.RootDirectoryOverride ?? DefaultRoot();
        Directory.CreateDirectory(root);
        return root;
    }

    public string ResolveDatabasePath()
    {
        var root = ResolveRoot();
        var path = Path.Combine(root, _options.DatabaseFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public string ResolveBlobRoot()
    {
        var blobRoot = Path.Combine(ResolveRoot(), _options.BlobSubdirectory);
        Directory.CreateDirectory(blobRoot);
        return blobRoot;
    }

    private static string DefaultRoot()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var portableMarker = Path.Combine(assemblyDir, PortableMarkerFileName);
        if (File.Exists(portableMarker))
        {
            return Path.Combine(assemblyDir, "data");
        }
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppDataFolderName);
    }
}
