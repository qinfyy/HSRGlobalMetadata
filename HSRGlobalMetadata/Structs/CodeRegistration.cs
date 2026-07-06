using HSRGlobalMetadata.Utils;

namespace HSRGlobalMetadata.Structs;

public class CodeRegistration : MetadataBase {
    private static CodeRegistration? _instance;
    public static CodeRegistration Instance => _instance ?? throw new Exception("Not initialized");

    public CodeRegistration(byte[] bytes) : base(bytes) {
        Populate();
    }
    
    public static void Initialize(string gameAssemblyPath) {
        if (_instance != null) return;
        ulong codeRegistrationOffset = PEHelper.RvaToOffset(StaticLayout.Instance.CodeRegistrationRva);
        if (codeRegistrationOffset == ulong.MaxValue) {
            throw new Exception("无法读取 CodeRegistration");
        }
        byte[] bytes = new ArraySegment<byte>(MetadataContext.Instance.GameAssembly, (int)codeRegistrationOffset, 0x100).ToArray();
        _instance = new CodeRegistration(bytes);
    }

    private long ReadRvaPtr(int offset) => BitConverter.ToInt64(_bytes, offset) - (long)PEHelper.ImageBase;

    public long MethodPointer { get; private set; }

    protected override void PostProcess() {
        MethodPointer = ReadRvaPtr(0x88);
        Console.WriteLine($"  method pointer table RVA: 0x{MethodPointer:X}");
    }
}
