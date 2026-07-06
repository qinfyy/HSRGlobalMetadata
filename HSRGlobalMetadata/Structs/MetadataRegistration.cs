using HSRGlobalMetadata.Utils;

namespace HSRGlobalMetadata.Structs;

public class MetadataRegistration : MetadataBase {
    private static MetadataRegistration? _instance;
    public static MetadataRegistration Instance => _instance ?? throw new Exception("Not initialized");

    public MetadataRegistration(byte[] bytes) : base(bytes) {
        Populate();
    }
    
    public static void Initialize(string gameAssemblyPath) {
        ulong descriptorOffset = PEHelper.RvaToOffset(StaticLayout.Instance.DescriptorRva);
        if (descriptorOffset == ulong.MaxValue) {
            throw new Exception("无法读取 MetadataRegistration descriptor");
        }
        byte[] bytes = new ArraySegment<byte>(MetadataContext.Instance.GameAssembly, (int)descriptorOffset, 0x100).ToArray();
        _instance = new MetadataRegistration(bytes);
    }

    private long ReadRvaPtr(int offset) => BitConverter.ToInt64(_bytes, offset) - (long)PEHelper.ImageBase;

    [MetadataTag(0x80, MetadataOperation.SUB, 458010256)]
    public int TypeInfoCount { get; set; }
    
    public long TypesRva { get; private set; }
    public long GenericInstsOffset { get; private set; }
    public long ArrayOffset { get; set; }
    
    protected override void PostProcess() {
        GenericInstsOffset = ReadRvaPtr(0x20);
        TypesRva = ReadRvaPtr(0x68);
        ArrayOffset = ReadRvaPtr(0x90);
        Console.WriteLine($"  type info count: {TypeInfoCount}");
        Console.WriteLine($"  type table RVA: 0x{TypesRva:X}");
        Console.WriteLine($"  generic inst table RVA: 0x{GenericInstsOffset:X}");
    }
}
