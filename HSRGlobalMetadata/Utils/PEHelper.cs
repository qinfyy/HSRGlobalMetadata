namespace HSRGlobalMetadata.Utils;

public struct SectionTable {
    public uint virtualAddr;
    public uint virtualSize;
    public uint sizeOfRawData;
    public uint ptrToRawData;
}

public static class PEHelper {
    public static SectionTable[] sectionTables = [];
    public static ulong ImageBase { get; private set; } = 0x180000000UL;
    
    public static ulong RvaToOffset(uint rva) {
        if (sectionTables == null || sectionTables.Length == 0)
            return 0;
        if (rva == 0) return 0;

        foreach (var section in sectionTables) {
            ulong sectionSize = Math.Max(section.virtualSize, section.sizeOfRawData);
            ulong sumAddr = section.virtualAddr + sectionSize;
        
            if (rva >= section.virtualAddr && rva < sumAddr) {
                ulong offset = section.ptrToRawData + (rva - section.virtualAddr);
                return offset;
            }
        }
        return ulong.MaxValue;
    }
    
    public static uint OffsetToRva(ulong fileOffset) {
        if (sectionTables == null || sectionTables.Length == 0)
            return 0;
        foreach (var section in sectionTables) {
            if (fileOffset >= section.ptrToRawData && fileOffset < section.ptrToRawData + section.sizeOfRawData) {
                return (uint)(section.virtualAddr + (fileOffset - section.ptrToRawData));
            }
        }
        return 0;
    }

    public static SectionTable[] ReadPEHeader(string dllPath) {
        using (var reader = new BinaryReader(File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read))) {
            reader.BaseStream.Seek(0x3C, SeekOrigin.Begin);
            uint peOffset = reader.ReadUInt32();
        
            reader.BaseStream.Seek(peOffset, SeekOrigin.Begin);
            uint signature = reader.ReadUInt32();
            if (signature != 0x4550) {
                throw new Exception("Invalid PE signature");
            }
            
            _ = reader.ReadUInt16();
            ushort numberOfSections = reader.ReadUInt16();
            reader.ReadBytes(12);
            ushort optionalHeaderSize = reader.ReadUInt16();
            reader.ReadUInt16();
        
            var optionalHeaderOffset = reader.BaseStream.Position;
            ushort magic = reader.ReadUInt16();
            if (magic == 0x20B) {
                reader.BaseStream.Seek(optionalHeaderOffset + 0x18, SeekOrigin.Begin);
                ImageBase = reader.ReadUInt64();
            } else {
                reader.BaseStream.Seek(optionalHeaderOffset + 0x1C, SeekOrigin.Begin);
                ImageBase = reader.ReadUInt32();
            }

            reader.BaseStream.Seek(optionalHeaderOffset + optionalHeaderSize, SeekOrigin.Begin);
        
            var sections = new SectionTable[numberOfSections];
            for (int i = 0; i < numberOfSections; i++) {
                reader.ReadBytes(8);
                sections[i].virtualSize = reader.ReadUInt32();
                sections[i].virtualAddr = reader.ReadUInt32();
                sections[i].sizeOfRawData = reader.ReadUInt32();
                sections[i].ptrToRawData = reader.ReadUInt32();
                reader.ReadBytes(16);
            }

            sectionTables = sections;
        
            return sections;
        }
    }
    
    public static byte[] ReadBytes(string path, long offset, int count) {
        byte[] buffer = new byte[count];

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);

        int read = fs.Read(buffer, 0, count);
        if (read != count)
            throw new EndOfStreamException();

        return buffer;
    }
}
