using HSRGlobalMetadata.Output;
using HSRGlobalMetadata.Structs;

namespace HSRGlobalMetadata;

using Utils;

static class Program {
    static void Main(string[] args) {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var folderPath = args.Length > 0 && !string.IsNullOrEmpty(args[0])
            ? args[0]
            : Prompt();

        folderPath = folderPath.Trim('"');

        if (!Directory.Exists(folderPath)) {
            Console.WriteLine("游戏目录不存在。");
            return;
        }

        var gameAssemblyPath = Directory.GetFiles(folderPath, "GameAssembly.dll").FirstOrDefault();
        if (gameAssemblyPath == null) {
            Console.WriteLine("未找到 GameAssembly.dll。");
            return;
        }

        PEHelper.ReadPEHeader(gameAssemblyPath);
        StaticLayout.Initialize(gameAssemblyPath);

        string metadataPath = Path.Combine(
            folderPath,
            "StarRail_Data",
            "il2cpp_data",
            "Metadata",
            "global-metadata.dat"
        );
        
        if(!Path.Exists(metadataPath)) {
          Console.WriteLine("未找到 global-metadata.dat。");
          return;
        }

        string startupMetadataPath = Path.Combine(
            folderPath,
            "StarRail_Data",
            "il2cpp_data",
            "Metadata",
            "startup-metadata.dat"
        );

        if (!Path.Exists(startupMetadataPath)) {
          Console.WriteLine("未找到 startup-metadata.dat。");
          return;
        }           

        Console.WriteLine("初始化中...");
        MetadataContext.Initialize(metadataPath, startupMetadataPath, gameAssemblyPath);
        MetadataHeader.Initialize(gameAssemblyPath);
        MetadataRegistration.Initialize(gameAssemblyPath);
        CodeRegistration.Initialize(gameAssemblyPath);
        MetadataTables.Initialize(gameAssemblyPath);
        Console.WriteLine("初始化缓存...");
        MetadataCache.Initialize();
        Console.WriteLine("初始化完成。");

        Console.WriteLine("写出 dump.cs...");
        DumpWriter.Write(folderPath);

        Console.WriteLine("写出 stringliterals.json...");
        StringLiteralWriter.Write(folderPath);
        
        Console.WriteLine("完成。");
    }

    static string Prompt() {
        Console.Write("请输入游戏目录: ");
        return Console.ReadLine() ?? "";
    }
}
