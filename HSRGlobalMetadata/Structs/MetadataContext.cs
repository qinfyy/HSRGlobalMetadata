namespace HSRGlobalMetadata.Structs;

public class MetadataContext {
    public byte[] RawMetadata { get; private set; } = null!;
    public byte[] Metadata { get; private set; } = null!;
    public byte[] StartupMetadata { get; private set; } = null!;
    public byte[] GameAssembly { get; private set; } = null!;

    private static readonly Dictionary<string, byte[]> FileCache = new(StringComparer.OrdinalIgnoreCase);

    private static MetadataContext? _instance;
    public static MetadataContext Instance => _instance ?? throw new Exception("MetadataContext is not initialized");

    public static byte[] ReadAllBytesShared(string path) {
        if (!FileCache.TryGetValue(path, out var bytes)) {
            bytes = File.ReadAllBytes(path);
            FileCache[path] = bytes;
        }
        return bytes;
    }

    public static void Initialize(string metadataPath, string startupPath, string gameAssemblyPath) {
        var raw = ReadAllBytesShared(metadataPath);
        var payloadOffset = (int)StaticLayout.Instance.PayloadOffset;
        if (payloadOffset < 0 || payloadOffset >= raw.Length) {
            throw new Exception($"metadata payload offset 越界: 0x{payloadOffset:X}");
        }

        var metadata = new byte[raw.Length - payloadOffset];
        Buffer.BlockCopy(raw, payloadOffset, metadata, 0, metadata.Length);

        _instance = new MetadataContext {
            RawMetadata = raw,
            Metadata = metadata,
            StartupMetadata = ReadAllBytesShared(startupPath),
            GameAssembly = ReadAllBytesShared(gameAssemblyPath)
        };
    }
}
