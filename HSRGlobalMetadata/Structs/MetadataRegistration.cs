using HSRGlobalMetadata.Utils;

namespace HSRGlobalMetadata.Structs;

public class MetadataRegistration : MetadataBase {
    private static MetadataRegistration? _instance;
    public static MetadataRegistration Instance => _instance ?? throw new Exception("Not initialized");

    private const int TypeEntryStrideValue = 16;

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

    public int TypeInfoCount { get; private set; }
    public int TypeEntryStride { get; private set; } = TypeEntryStrideValue;

    public long TypesRva { get; private set; }
    public long GenericInstsOffset { get; private set; }
    public long ArrayOffset { get; set; }

    protected override void PostProcess() {
        // 4.4.51: types@+0x80 / gi@+0x38 / array@+0x70 / stride=16
        GenericInstsOffset = ReadRvaPtr(0x38);
        TypesRva = ReadRvaPtr(0x80);
        ArrayOffset = ReadRvaPtr(0x70);
        TypeEntryStride = TypeEntryStrideValue;
        TypeInfoCount = ScanTypeTableCount(MetadataContext.Instance.GameAssembly, TypesRva);
        if (TypeInfoCount < 100000) {
            throw new Exception($"type_info_count 扫描结果异常: {TypeInfoCount}");
        }
        Console.WriteLine($"  type entry stride: {TypeEntryStride}");
        Console.WriteLine($"  type info count: {TypeInfoCount}");
        Console.WriteLine($"  type table RVA: 0x{TypesRva:X}");
        Console.WriteLine($"  generic inst table RVA: 0x{GenericInstsOffset:X}");
        Console.WriteLine($"  array table RVA: 0x{ArrayOffset:X}");
    }

    private static bool IsValidTypeEntry(byte[] gameAssembly, int offset) {
        if (offset < 0 || offset + TypeEntryStrideValue > gameAssembly.Length) return false;
        ulong data = BitConverter.ToUInt64(gameAssembly, offset);
        ulong meta = BitConverter.ToUInt64(gameAssembly, offset + 8);
        if ((meta >> 32) != 0) return false;
        byte type = (byte)(meta >> 16);
        if (type == 0) return data == 0;
        return type >= 1 && type <= 0x1E;
    }

    private static int ScanTypeTableCount(byte[] gameAssembly, long typesRva, int maxInvalidStreak = 64) {
        if (typesRva <= 0 || typesRva >= 0x20000000L) return 0;
        ulong fileOffset = PEHelper.RvaToOffset((uint)typesRva);
        if (fileOffset == ulong.MaxValue) return 0;
        int off = (int)fileOffset;
        int lastGood = -1;
        int streak = 0;
        int maxEntries = (gameAssembly.Length - off) / TypeEntryStrideValue;
        for (int i = 0; i < maxEntries; i++) {
            if (IsValidTypeEntry(gameAssembly, off + i * TypeEntryStrideValue)) {
                lastGood = i;
                streak = 0;
                continue;
            }
            streak++;
            if (streak > maxInvalidStreak) break;
        }
        return lastGood >= 0 ? lastGood + 1 : 0;
    }
}
