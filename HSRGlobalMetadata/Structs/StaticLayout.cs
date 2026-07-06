using HSRGlobalMetadata.Utils;

namespace HSRGlobalMetadata.Structs;

public sealed class StaticLayout {
    private static StaticLayout? _instance;
    public static StaticLayout Instance => _instance ?? throw new Exception("StaticLayout 未初始化");

    public uint StaticInitializerRva { get; private set; }
    public uint CodeRegistrationRva { get; private set; }
    public uint DescriptorRva { get; private set; }
    public uint MetadataTablesRva { get; private set; }
    public uint EmbeddedHeaderRva { get; private set; }
    public uint PayloadOffset { get; private set; }
    public ushort MethodAttributeXor { get; private set; }

    private StaticLayout() {
    }

    public static void Initialize(string gameAssemblyPath) {
        if (_instance != null) return;

        var bytes = MetadataContext.ReadAllBytesShared(gameAssemblyPath);
        var layout = DiscoverStaticMetadataGlobals(bytes);
        layout.PayloadOffset = DiscoverExternalPayloadOffset(bytes);
        layout.MethodAttributeXor = DiscoverMethodAttributeXor(bytes);
        _instance = layout;

        Console.WriteLine($"Static layout:");
        Console.WriteLine($"  static initializer RVA: 0x{layout.StaticInitializerRva:X}");
        Console.WriteLine($"  code registration RVA: 0x{layout.CodeRegistrationRva:X}");
        Console.WriteLine($"  descriptor RVA: 0x{layout.DescriptorRva:X}");
        Console.WriteLine($"  metadata tables RVA: 0x{layout.MetadataTablesRva:X}");
        Console.WriteLine($"  embedded header RVA: 0x{layout.EmbeddedHeaderRva:X}");
        Console.WriteLine($"  external payload offset: 0x{layout.PayloadOffset:X}");
        Console.WriteLine($"  method attribute xor: 0x{layout.MethodAttributeXor:X}");
    }

    private static StaticLayout DiscoverStaticMetadataGlobals(byte[] bytes) {
        var pattern = StaticMetadataInitializerPattern();
        foreach (var initializerRva in PatternScanner.FindPatternRvas(bytes, pattern)) {
            uint codeRegistrationRva = ReadLeaTargetRva(bytes, initializerRva);
            uint descriptorRva = ReadLeaTargetRva(bytes, initializerRva + 14);
            uint metadataTablesRva = ReadLeaTargetRva(bytes, initializerRva + 28);
            uint embeddedHeaderRva = ReadLeaTargetRva(bytes, initializerRva + 56);

            uint descriptorCount = unchecked(ReadUInt32Rva(bytes, descriptorRva + 0x80) + 0xE4B35170u);
            uint methodSpan = ReadUInt32Rva(bytes, embeddedHeaderRva + 0x1F8) ^ 0x1608C2C8u;
            if (descriptorCount > 100000 && methodSpan > 1000000) {
                return new StaticLayout {
                    StaticInitializerRva = initializerRva,
                    CodeRegistrationRva = codeRegistrationRva,
                    DescriptorRva = descriptorRva,
                    MetadataTablesRva = metadataTablesRva,
                    EmbeddedHeaderRva = embeddedHeaderRva,
                };
            }
        }

        throw new Exception("无法定位当前版本的 IL2CPP 静态元数据初始化函数");
    }

    private static uint DiscoverExternalPayloadOffset(byte[] bytes) {
        var pattern = MetadataPayloadInitializerPattern();
        foreach (var initializerRva in PatternScanner.FindPatternRvas(bytes, pattern)) {
            uint globalNameRva = ReadLeaTargetRva(bytes, initializerRva);
            uint startupNameRva = ReadLeaTargetRva(bytes, initializerRva + 15);
            if (!RvaHasCString(bytes, globalNameRva, "global-metadata.dat")) continue;
            if (!RvaHasCString(bytes, startupNameRva, "startup-metadata.dat")) continue;

            uint payloadOffset = ReadUInt32Rva(bytes, initializerRva + 46);
            if (payloadOffset != 0) return payloadOffset;
        }

        throw new Exception("无法定位当前版本的 metadata payload offset");
    }

    private static byte?[] StaticMetadataInitializerPattern() {
        var pattern = Enumerable.Repeat<byte?>(null, 80).ToArray();
        foreach (int offset in new[] { 0, 14, 28, 42, 56 }) {
            pattern[offset] = 0x48;
            pattern[offset + 1] = 0x8D;
            pattern[offset + 2] = 0x05;
            pattern[offset + 7] = 0x48;
            pattern[offset + 8] = 0x89;
            pattern[offset + 9] = 0x05;
        }
        pattern[70] = 0xC7;
        pattern[71] = 0x05;
        return pattern;
    }

    private static byte?[] MetadataPayloadInitializerPattern() {
        return [
            0x48, 0x8D, 0x0D, null, null, null, null,
            0xE8, null, null, null, null,
            0x48, 0x89, 0xC6,
            0x48, 0x8D, 0x0D, null, null, null, null,
            0xE8, null, null, null, null,
            0x48, 0x8B, 0x0D, null, null, null, null,
            0x8B, 0x49, 0x04,
            0x89, 0x0D, null, null, null, null,
            0x48, 0x81, 0xC6, null, null, null, null,
            0x48, 0x89, 0x35, null, null, null, null,
            0x48, 0x89, 0x05, null, null, null, null,
        ];
    }

    private static ushort DiscoverMethodAttributeXor(byte[] bytes) {
        var pattern = MethodAttributeXorPattern();
        foreach (var instructionRva in PatternScanner.FindPatternRvas(bytes, pattern)) {
            uint targetRva = ReadRipRelativeTargetRva(bytes, instructionRva, 8);
            return ReadUInt16Rva(bytes, targetRva);
        }

        throw new Exception("无法定位当前版本的方法属性解密常量");
    }

    private static byte?[] MethodAttributeXorPattern() {
        return [
            0x66, 0x0F, 0x6F, 0x05, null, null, null, null,
            0x4C, 0x89, 0xC5,
            0x4C, 0x89, 0x44, 0x24, 0x38,
        ];
    }

    private static bool RvaHasCString(byte[] bytes, uint rva, string expected) {
        ulong offset = PEHelper.RvaToOffset(rva);
        if (offset == ulong.MaxValue) return false;
        if (offset + (ulong)expected.Length >= (ulong)bytes.Length) return false;

        for (int i = 0; i < expected.Length; i++) {
            if (bytes[(int)offset + i] != expected[i]) return false;
        }
        return bytes[(int)offset + expected.Length] == 0;
    }

    private static uint ReadLeaTargetRva(byte[] bytes, uint instructionRva) {
        ulong offset = PEHelper.RvaToOffset(instructionRva);
        if (offset == ulong.MaxValue || offset + 7 > (ulong)bytes.Length) {
            throw new Exception($"LEA 指令 RVA 越界: 0x{instructionRva:X}");
        }

        if (bytes[(int)offset] != 0x48 || bytes[(int)offset + 1] != 0x8D || (bytes[(int)offset + 2] & 0xC7) != 0x05) {
            throw new Exception($"预期 RIP-relative LEA，实际 RVA: 0x{instructionRva:X}");
        }

        int displacement = BitConverter.ToInt32(bytes, (int)offset + 3);
        long target = instructionRva + 7L + displacement;
        if (target < 0 || target > uint.MaxValue) {
            throw new Exception($"RIP-relative LEA 目标越界: 0x{target:X}");
        }
        return (uint)target;
    }

    private static uint ReadRipRelativeTargetRva(byte[] bytes, uint instructionRva, uint instructionLength) {
        ulong offset = PEHelper.RvaToOffset(instructionRva);
        if (offset == ulong.MaxValue || offset + instructionLength > (ulong)bytes.Length) {
            throw new Exception($"RIP-relative 指令 RVA 越界: 0x{instructionRva:X}");
        }

        int displacement = BitConverter.ToInt32(bytes, (int)(offset + instructionLength - 4));
        long target = instructionRva + (long)instructionLength + displacement;
        if (target < 0 || target > uint.MaxValue) {
            throw new Exception($"RIP-relative 目标越界: 0x{target:X}");
        }
        return (uint)target;
    }

    private static uint ReadUInt32Rva(byte[] bytes, uint rva) {
        ulong offset = PEHelper.RvaToOffset(rva);
        if (offset == ulong.MaxValue || offset + 4 > (ulong)bytes.Length) {
            throw new Exception($"读取 RVA 越界: 0x{rva:X}");
        }
        return BitConverter.ToUInt32(bytes, (int)offset);
    }

    private static ushort ReadUInt16Rva(byte[] bytes, uint rva) {
        ulong offset = PEHelper.RvaToOffset(rva);
        if (offset == ulong.MaxValue || offset + 2 > (ulong)bytes.Length) {
            throw new Exception($"读取 RVA 越界: 0x{rva:X}");
        }
        return BitConverter.ToUInt16(bytes, (int)offset);
    }
}
