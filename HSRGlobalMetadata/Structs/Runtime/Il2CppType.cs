using HSRGlobalMetadata.Structs.Definitions;
using HSRGlobalMetadata.Utils;
using System.Collections.Concurrent;

namespace HSRGlobalMetadata.Structs.Runtime;

public class Il2CppType {
    static readonly ConcurrentDictionary<int, string> GenericClassNameCache = new();

    static readonly Dictionary<byte, string> PrimitiveTypes = new() {
        [0x01] = "void",
        [0x02] = "bool",
        [0x03] = "char",
        [0x04] = "sbyte",
        [0x05] = "byte",
        [0x06] = "short",
        [0x07] = "ushort",
        [0x08] = "int",
        [0x09] = "uint",
        [0x0A] = "long",
        [0x0B] = "ulong",
        [0x0C] = "float",
        [0x0D] = "double",
        [0x0E] = "string",
        [0x16] = "TypedReference",
        [0x18] = "IntPtr",
        [0x19] = "UIntPtr",
        [0x1C] = "object",
    };
    
    public ulong Data;
    public ushort Attrs;
    public byte Type;
    
    private string _cachedName;

    public Il2CppType(int offset) {
        ReadEntry(offset, out Data, out Attrs, out Type);
    }

    public static Il2CppType FromIndex(int index) {
        if (index < 0 || index >= MetadataRegistration.Instance.TypeInfoCount) {
            throw new ArgumentOutOfRangeException($"{nameof(index)}, value: {index}"); 
        }
        if (MetadataCache.Types != null && index < MetadataCache.Types.Length && MetadataCache.Types[index] != null)
            return MetadataCache.Types[index];
        int offset = (int)PEHelper.RvaToOffset((uint)(MetadataRegistration.Instance.TypesRva + index * 8));

        return new Il2CppType(offset);
    }

    public static Il2CppType FromMetadataIndex(int index) {
        if (index < 0) {
            throw new ArgumentOutOfRangeException($"{nameof(index)}, value: {index}");
        }

        return FromIndex(index);
    }

    public static Il2CppType FromSignatureIndex(int index) {
        if (index < 0) {
            throw new ArgumentOutOfRangeException($"{nameof(index)}, value: {index}");
        }

        return FromIndex(index);
    }

    private static void ReadEntry(int offset, out ulong data, out ushort attrs, out byte type) {
        var bytes = MetadataContext.Instance.GameAssembly;
        ulong rawData = BitConverter.ToUInt64(bytes, offset);
        attrs = (ushort)(rawData >> 32);
        type = (byte)(rawData >> 48);
        data = ResolveData(rawData);

        if (rawData < PEHelper.ImageBase || TryTypePointerToIndex(rawData, out _)) return;

        ulong pointedOffset = PEHelper.RvaToOffset((uint)(rawData - PEHelper.ImageBase));
        if (pointedOffset == ulong.MaxValue || pointedOffset + 8 > (ulong)bytes.Length) return;

        ulong pointedRawData = BitConverter.ToUInt64(bytes, (int)pointedOffset);
        data = ResolvePointedData(pointedRawData);
        attrs = (ushort)(pointedRawData >> 32);
        type = (byte)(pointedRawData >> 48);
    }

    private static ulong ResolveData(ulong rawData) {
        if (rawData < PEHelper.ImageBase) return rawData & 0xFFFFFFFFUL;
        return TryTypePointerToIndex(rawData, out var index) ? index : rawData;
    }

    private static ulong ResolvePointedData(ulong rawData) {
        if (rawData < PEHelper.ImageBase) return rawData & 0xFFFFFFFFUL;
        return TryTypePointerToIndex(rawData, out var index) ? index : (uint)rawData;
    }

    private static bool TryTypePointerToIndex(ulong typeVa, out ulong index) {
        index = 0;
        if (typeVa < PEHelper.ImageBase) return false;

        ulong typeRva = typeVa - PEHelper.ImageBase;
        long byteOffset = (long)typeRva - MetadataRegistration.Instance.TypesRva;
        if (byteOffset < 0 || byteOffset % 8 != 0) return false;

        long typeIndex = byteOffset / 8;
        if (typeIndex < 0 || typeIndex >= MetadataRegistration.Instance.TypeInfoCount) return false;

        index = (ulong)typeIndex;
        return true;
    }

    private static bool IsValidTypeDefinitionIndex(int index) {
        return MetadataCache.TypeDefs != null && index >= 0 && index < MetadataCache.TypeDefs.Length;
    }

    private static string SafeTypeName(int typeIndex) {
        if (typeIndex < 0 || typeIndex >= MetadataRegistration.Instance.TypeInfoCount) return $"Il2CppType_{typeIndex}";
        return FromIndex(typeIndex).Name();
    }

    private static string SafeTypeDefName(int typeDefinitionIndex) {
        if (!IsValidTypeDefinitionIndex(typeDefinitionIndex)) return $"TypeDef_{typeDefinitionIndex}";
        return ResolveTypeDefName(typeDefinitionIndex);
    }

    private static string SafeGenericParameterName(int parameterIndex) {
        int genericParamOffset = MetadataHeader.Instance.GenericParametersOffset + parameterIndex * 14;
        if (genericParamOffset < 0 || genericParamOffset + 4 > MetadataContext.Instance.Metadata.Length) return $"T{parameterIndex}";

        int nameIndex = BitConverter.ToInt32(MetadataContext.Instance.Metadata, genericParamOffset);
        int scramble = (int)(((ulong)(1252900171 *
                    ((((0x617FE3CC452CL * (ulong)parameterIndex + 0x9DC5DB71F0EB440L) >> 9)
                      + 718849585)
                     ^ 0x5278374D))) >> 15)
                    + 1149796643;

        int finalIndex = nameIndex - scramble;
        string name = StringProcessor.Decrypt(finalIndex);
        return name.Length != 0 ? name : $"T{parameterIndex}";
    }

    private static bool IsReadableRva(ulong rva, int size, out int offset) {
        offset = 0;
        ulong fileOffset = PEHelper.RvaToOffset((uint)rva);
        if (fileOffset == ulong.MaxValue || fileOffset + (ulong)size > (ulong)MetadataContext.Instance.GameAssembly.Length) return false;

        offset = (int)fileOffset;
        return true;
    }

    private static bool IsReadableStartupOffset(int offset, int size) {
        return offset >= 0 && offset + size <= MetadataContext.Instance.StartupMetadata.Length;
    }

    private static string JoinGenericArguments(int argCount, int arrayOffset) {
        var args = new List<string>(argCount);
        for (int i = 0; i < argCount; i++) {
            long ptr = BitConverter.ToInt64(MetadataContext.Instance.GameAssembly, arrayOffset + i * 8);
            ulong typeArgOffset = PEHelper.RvaToOffset((uint)(ptr - (long)PEHelper.ImageBase));
            if (typeArgOffset == ulong.MaxValue) {
                args.Add("object");
                continue;
            }

            args.Add(new Il2CppType((int)typeArgOffset).Name());
        }

        return string.Join(", ", args);
    }
    
    public string Name() {
        if (_cachedName != null) return _cachedName;
        _cachedName = ComputeName();
        return _cachedName;
    }

    public string ComputeName() {
        if (PrimitiveTypes.TryGetValue(Type, out var name))
            return name;

        switch (Type) {
            case 0x11:
            case 0x12:
            case 0x1C:
                return SafeTypeDefName((int)Data);
            
            case 0x15:
                int genericClassIndex = (int)Data;

                if (GenericClassNameCache.TryGetValue(genericClassIndex, out var cached))
                    return cached;

                int baseOffset = MetadataHeader.Instance.GenericClassOffset + genericClassIndex * 8;
                if (!IsReadableStartupOffset(baseOffset, 8)) return $"GenericInst_{genericClassIndex}";
                
                int instIndex = BitConverter.ToInt32(MetadataContext.Instance.StartupMetadata, baseOffset + 4);
                int typeDefIndex = BitConverter.ToInt32(MetadataContext.Instance.StartupMetadata, baseOffset);
                
                string openName = SafeTypeDefName(typeDefIndex);
                int tick = openName.IndexOf('`');
                if (tick >= 0) openName = openName[..tick];

                if (instIndex == -1)
                    return openName;
                
                int instOffset = (int)PEHelper.RvaToOffset((uint)MetadataRegistration.Instance.GenericInstsOffset) + instIndex * 16;
                if (instOffset < 0 || instOffset + 16 > MetadataContext.Instance.GameAssembly.Length) return openName;

                int argCount = BitConverter.ToInt32(MetadataContext.Instance.GameAssembly, instOffset);
                long arrayRva = BitConverter.ToInt64(MetadataContext.Instance.GameAssembly, instOffset + 8);

                int arrayOffset = (int)PEHelper.RvaToOffset((uint)(arrayRva - (long)PEHelper.ImageBase));
                if (argCount < 0 || argCount > 128 || arrayOffset < 0 || arrayOffset + argCount * 8 > MetadataContext.Instance.GameAssembly.Length) return openName;

                string result = $"{openName}<{JoinGenericArguments(argCount, arrayOffset)}>";
                GenericClassNameCache[genericClassIndex] = result;
                return result;
            
            case 0x0F:
                if (Data == 0) return "void*";
                return SafeTypeName((int)Data) + "*";
            
            case 0x14:
                ulong arrayEntryRva = (ulong)MetadataRegistration.Instance.ArrayOffset + Data * 32;
                if (!IsReadableRva(arrayEntryRva, 16, out int arrayEntryOffset)) return "object[]";

                long arrayElemPtr = BitConverter.ToInt64(MetadataContext.Instance.GameAssembly, (int)arrayEntryOffset);
                ulong arrayElemOffset = PEHelper.RvaToOffset((uint)(arrayElemPtr - (long)PEHelper.ImageBase));
                if (arrayElemOffset == ulong.MaxValue) return "object[]";

                int arrayRank = MetadataContext.Instance.GameAssembly[(int)arrayEntryOffset + 8];
                return $"{new Il2CppType((int)arrayElemOffset).Name()}[{new string(',', arrayRank - 1)}]";
            
            case 0x1D:
                return SafeTypeName((int)Data) + "[]";


            case 0x10:
                if (Data < PEHelper.ImageBase) return SafeTypeName((int)Data);

                ulong innerRva = Data - PEHelper.ImageBase;
                ulong innerOffset = PEHelper.RvaToOffset((uint)innerRva);
                if (innerOffset == ulong.MaxValue) return "TypedReference";

                return new Il2CppType((int)innerOffset).Name();
            
            case 0x13:
            case 0x1E:
                return SafeGenericParameterName((int)Data);
            
            default:
                return "object";
        }
    }

    public static string ResolveTypeDefName(int typeDefinitionIndex) {
        if (!IsValidTypeDefinitionIndex(typeDefinitionIndex)) return $"TypeDef_{typeDefinitionIndex}";
        Il2CppTypeDefinition typeDef = new Il2CppTypeDefinition(typeDefinitionIndex);

        string ns = typeDef.Namespace;
        string name = typeDef.Name;

        if (!string.IsNullOrEmpty(ns))
            return ns + "." + name;

        return name;
    }
}
