using HSRGlobalMetadata.Utils;

namespace HSRGlobalMetadata.Structs;

public class MetadataTables : MetadataBase
{
    private static MetadataTables? _instance;
    public static MetadataTables Instance => _instance ?? throw new Exception("MetadataTables not initialized");

    [MetadataTag(0x28)]
    public long StringLiteralVa { get; private set; }

    public uint StringLiteralRva { get; private set; }

    [MetadataTag(0x40, MetadataOperation.XOR, 0xBD08DC8)]
    public int StringLiteralCount { get; private set; }

    public MetadataTables(byte[] bytes) : base(bytes)
    {
        Populate();
    }

    protected override void PostProcess()
    {
        if ((ulong)StringLiteralVa >= PEHelper.ImageBase)
        {
            StringLiteralRva = (uint)((ulong)StringLiteralVa - PEHelper.ImageBase);
        }
    }

    public static void Initialize(string gameAssemblyPath)
    {
        ulong metadataTablesOffset = PEHelper.RvaToOffset(StaticLayout.Instance.MetadataTablesRva);
        if (metadataTablesOffset == ulong.MaxValue)
        {
            throw new Exception("无法读取 MetadataTables");
        }
        byte[] bytes = new ArraySegment<byte>(MetadataContext.Instance.GameAssembly, (int)metadataTablesOffset, 0x68).ToArray();
        _instance = new MetadataTables(bytes);
        Console.WriteLine($"  string literal RVA: 0x{_instance.StringLiteralRva:X}");
        Console.WriteLine($"  string literal count: {_instance.StringLiteralCount}");
    }
}
