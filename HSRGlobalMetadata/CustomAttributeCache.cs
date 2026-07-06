using HSRGlobalMetadata.Structs;
using HSRGlobalMetadata.Structs.Runtime;

namespace HSRGlobalMetadata;

public sealed class CustomAttributeCache {
    private const byte TypeDefTable = 0x02;
    private const byte FieldTable = 0x04;
    private const byte MethodTable = 0x06;
    private const byte ParameterTable = 0x08;
    private const byte EventTable = 0x14;
    private const byte PropertyTable = 0x17;
    private const byte AssemblyTable = 0x20;

    private static readonly HashSet<byte> ValidTokenTables = [
        TypeDefTable,
        FieldTable,
        MethodTable,
        ParameterTable,
        EventTable,
        PropertyTable,
        AssemblyTable
    ];

    private readonly Dictionary<uint, string[]> _attributesByToken;

    public bool Enabled { get; }

    private CustomAttributeCache(bool enabled, Dictionary<uint, string[]> attributesByToken) {
        Enabled = enabled;
        _attributesByToken = attributesByToken;
    }

    public static CustomAttributeCache Create() {
        try {
            return CreateCore();
        } catch (Exception ex) {
            Console.WriteLine($"Custom Attribute 解析失败，已跳过输出: {ex.Message}");
            return new CustomAttributeCache(false, []);
        }
    }

    public IReadOnlyList<string> Get(uint token) {
        if (!Enabled) return Array.Empty<string>();
        return _attributesByToken.TryGetValue(token, out var attrs) ? attrs : Array.Empty<string>();
    }

    public static uint TypeToken(int typeDefinitionIndex) => MakeToken(TypeDefTable, typeDefinitionIndex);

    public static uint FieldToken(int fieldIndex) => MakeToken(FieldTable, fieldIndex);

    public static uint MethodToken(int methodIndex) => MakeToken(MethodTable, methodIndex);

    public static uint EventToken(int eventIndex) => MakeToken(EventTable, eventIndex);

    public static uint PropertyToken(int propertyIndex) => MakeToken(PropertyTable, propertyIndex);

    private static CustomAttributeCache CreateCore() {
        var ranges = ReadRanges();
        if (ranges.Count == 0) {
            Console.WriteLine("Custom Attribute range 表为空，已跳过输出。");
            return new CustomAttributeCache(false, []);
        }

        var result = new Dictionary<uint, string[]>(ranges.Count);
        var conflictedTokens = new HashSet<uint>();
        int resolvedTypeCount = 0;
        int attributeLikeCount = 0;
        int invalidTypeCount = 0;

        foreach (var range in ranges) {
            if (conflictedTokens.Contains(range.Token)) continue;

            var names = ResolveAttributeNames(range, ref resolvedTypeCount, ref attributeLikeCount, ref invalidTypeCount);
            if (names.Length == 0) continue;

            if (result.TryAdd(range.Token, names)) continue;

            result.Remove(range.Token);
            conflictedTokens.Add(range.Token);
        }

        int checkedCount = resolvedTypeCount + invalidTypeCount;
        if (checkedCount == 0 || resolvedTypeCount * 100 / checkedCount < 80 || attributeLikeCount * 100 / checkedCount < 40) {
            Console.WriteLine($"Custom Attribute 表校验失败，已跳过输出: resolved={resolvedTypeCount}, attributeLike={attributeLikeCount}, invalid={invalidTypeCount}");
            return new CustomAttributeCache(false, []);
        }

        Console.WriteLine($"Custom Attribute: ranges={ranges.Count}, tokens={result.Count}, resolved={resolvedTypeCount}, conflicts={conflictedTokens.Count}");
        return new CustomAttributeCache(true, result);
    }

    private static List<CustomAttributeRange> ReadRanges() {
        var metadata = MetadataContext.Instance.Metadata;
        int offset = MetadataHeader.Instance.CustomAttributeRangeOffset;
        var ranges = new List<CustomAttributeRange>(4096);
        int previousStart = -1;

        for (int index = 0; offset + index * 8 + 8 <= metadata.Length; index++) {
            uint encodedRange = BitConverter.ToUInt32(metadata, offset + index * 8);
            uint token = BitConverter.ToUInt32(metadata, offset + index * 8 + 4);
            int count = (int)(encodedRange >> 24);
            int start = (int)(encodedRange & 0x00FFFFFF);
            byte table = (byte)(token >> 24);
            int rid = (int)(token & 0x00FFFFFF);

            if (count <= 0 || count > 255 || start < previousStart || rid <= 0 || !ValidTokenTables.Contains(table)) break;
            if (!IsValidAttributeTypeRange(start, count)) break;

            ranges.Add(new CustomAttributeRange(token, start, count));
            previousStart = start;
        }

        return ranges;
    }

    private static bool IsValidAttributeTypeRange(int start, int count) {
        long byteOffset = (long)MetadataHeader.Instance.CustomAttributeTypesOffset + (long)start * 4;
        long byteEnd = byteOffset + (long)count * 4;
        return byteOffset >= 0 && byteEnd <= MetadataContext.Instance.Metadata.Length;
    }

    private static string[] ResolveAttributeNames(CustomAttributeRange range, ref int resolvedTypeCount, ref int attributeLikeCount, ref int invalidTypeCount) {
        var metadata = MetadataContext.Instance.Metadata;
        int baseOffset = MetadataHeader.Instance.CustomAttributeTypesOffset + range.Start * 4;
        var names = new List<string>(range.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < range.Count; i++) {
            int typeIndex = BitConverter.ToInt32(metadata, baseOffset + i * 4);
            if (typeIndex < 0 || typeIndex >= MetadataRegistration.Instance.TypeInfoCount) {
                invalidTypeCount++;
                continue;
            }

            string fullName = Il2CppType.FromIndex(typeIndex).Name();
            string displayName = FormatAttributeName(fullName);
            if (displayName.Length == 0) {
                invalidTypeCount++;
                continue;
            }

            resolvedTypeCount++;
            if (fullName.EndsWith("Attribute", StringComparison.Ordinal) || displayName.EndsWith("Attribute", StringComparison.Ordinal))
                attributeLikeCount++;

            if (seen.Add(displayName))
                names.Add(displayName);
        }

        return names.ToArray();
    }

    private static string FormatAttributeName(string fullName) {
        if (string.IsNullOrWhiteSpace(fullName)) return "";
        if (fullName is "object" or "void") return "";
        if (fullName.StartsWith("Il2CppType_", StringComparison.Ordinal)) return "";
        if (fullName.StartsWith("TypeDef_", StringComparison.Ordinal)) return "";

        int dot = fullName.LastIndexOf('.');
        string name = dot >= 0 ? fullName[(dot + 1)..] : fullName;
        int nested = name.LastIndexOf('/');
        if (nested >= 0) name = name[(nested + 1)..];

        const string suffix = "Attribute";
        if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            name = name[..^suffix.Length];

        return name;
    }

    private static uint MakeToken(byte table, int zeroBasedIndex) {
        return ((uint)table << 24) | (uint)(zeroBasedIndex + 1);
    }

    private readonly record struct CustomAttributeRange(uint Token, int Start, int Count);
}
