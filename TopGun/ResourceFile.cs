using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;

namespace TopGun;

public enum ResourceArchitecture : ushort
{
    Bits16 = 0x3631,
    Bits32 = 0x3233,
    Grail2 = 2
}

public enum KeyResourceID
{
    Resources,
    Entries,
    IndexBuffers,
    Variables,
    ConstStrings,
    Scripts,
    Palette,
    NameTable,
    Unknown8,
    Unknown9,
    Plugins,
    PluginProcs,
    PluginIndexPerProc,
    Unknown13,
    SourceFile
}

public enum ResourceType : byte
{
    Bitmap = 1,
    Data,
    File,
    Frame,
    Ground,
    Midi,
    Model,
    MProto,
    Obj3D,
    OProto,
    Table,
    Wave,
    Movie,
    Array,
    Cell,
    Group,
    Palette,
    Queue,
    Script,
    Sprite,
    Text,
    Tile,
    Title,
    Subtitle,
    Local,
    Entry
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Resource
{
    public ResourceType Type;
    public byte Extension;
    public uint Offset;
    public uint Size;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct OffsetSize
{
    public uint Offset;
    public uint Size;
}

public readonly struct Plugin
{
    public readonly string Name;
    public readonly IReadOnlyList<string> Procs;

    public Plugin(string name, IReadOnlyList<string> procs)
    {
        Name = name;
        Procs = procs;
    }
}

public unsafe class ResourceFile
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private unsafe struct MetaHeader
    {
        public ushort Magic;
        public ushort HeaderSize;
        public ResourceArchitecture Architecture;
        public fixed byte Title[0x50];
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private unsafe struct ResHeaderNew32
    {
        public uint EntryId;
        public fixed uint ScriptEndOffsets[0x30];
        public uint ScriptCount;
        public uint MaxFadeColors;
        public uint MaxTransColors;
        public uint DynamicResources;
        public uint CountStrings;
        public uint CountVariables;
        public uint MaxScrMsg;
        public fixed uint Unknown1[8];
        public byte BuildType;
        public fixed byte Unknown2[11];
        public fixed ulong KeyResources[15];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private unsafe struct ResHeaderOld16
    {
        public fixed byte Unknown1[10];
        public fixed uint ScriptEndOffsets[0x20];
        public ushort ScriptCount;
        public ushort MaxFadeColors;
        public ushort MaxTransColors;
        public ushort DynamicResources;
        public ushort CountStrings;
        public ushort CountVariables;
        public byte BuildType;
        public fixed byte Unknown2[3];
        public fixed ulong KeyResources[14];

        public static explicit operator ResHeaderNew32(ResHeaderOld16 src)
        {
            ResHeaderNew32 dst = new()
            {
                ScriptCount = src.ScriptCount,
                MaxFadeColors = src.MaxFadeColors,
                MaxTransColors = src.MaxTransColors,
                DynamicResources = src.DynamicResources,
                CountStrings = src.CountStrings,
                CountVariables = src.CountVariables,
                BuildType = src.BuildType
            };
            Buffer.MemoryCopy(src.ScriptEndOffsets, dst.ScriptEndOffsets, 0x20 * sizeof(uint), 0x20 * sizeof(uint));
            Buffer.MemoryCopy(src.KeyResources, dst.KeyResources, 14 * sizeof(ulong), 14 * sizeof(ulong));
            return dst;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private unsafe struct ResHeaderGrail2
    {
        public fixed byte Unknown1[10];
        public fixed uint ScriptEndOffsets[0x18];
        public ushort ScriptCount;
        public ushort MaxFadeColors;
        public ushort MaxTransColors;
        public ushort DynamicResources;
        public ushort CountStrings;
        public ushort CountVariables;
        public byte BuildType;
        public fixed byte Unknown2[19];
        public fixed ulong KeyResources[12];

        public static explicit operator ResHeaderNew32(ResHeaderGrail2 src)
        {
            ResHeaderNew32 dst = new()
            {
                ScriptCount = src.ScriptCount,
                MaxFadeColors = src.MaxFadeColors,
                MaxTransColors = src.MaxTransColors,
                DynamicResources = src.DynamicResources,
                CountStrings = src.CountStrings,
                CountVariables = src.CountVariables,
                BuildType = src.BuildType
            };
            Buffer.MemoryCopy(src.ScriptEndOffsets, dst.ScriptEndOffsets, 0x18 * sizeof(uint), 0x18 * sizeof(uint));
            Buffer.MemoryCopy(src.KeyResources, dst.KeyResources, 12 * sizeof(ulong), 12 * sizeof(ulong));
            return dst;
        }
    }

    public ResourceArchitecture Architecture { get; }
    public ushort Version { get; }
    public string Title { get; }
    public string SubTitle { get; }
    public uint EntryId { get; }
    public uint MaxFadeColors { get; }
    public uint MaxTransColors { get; }
    public uint DynamicResources { get; }
    public uint CountStrings { get; }
    public uint CountVariables { get; }
    public uint MaxScrMsg { get; }
    public byte BuildType { get; }

    public IReadOnlyList<Resource> Resources { get; }
    public IReadOnlyList<byte[]> Entries { get; }
    public IReadOnlyList<byte[]> IndexBuffers { get; }
    public IReadOnlyList<KeyValuePair<uint, uint>> Variables { get; }
    public IReadOnlyList<string> ConstStrings { get; }
    public IReadOnlyList<byte[]> ScriptSections { get; }
    public IReadOnlyList<Rgba32> Palette { get; }
    public IReadOnlyList<string> NameTable { get; }
    public IReadOnlyList<Plugin> Plugins { get; }
    public IReadOnlyList<(Plugin plugin, int localProcIdx)> PluginProcs { get; }
    public string SourceFile { get; }
    public byte[] UnknownKeyResource8 { get; }
    public byte[] UnknownKeyResource9 { get; }
    public byte[] UnknownKeyResource13 { get; }

    private readonly string extensionBasePath;

    public ResourceFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var meta = ReadStruct<MetaHeader>(stream);
        if (meta.Magic != 0x4C37)
            throw new InvalidDataException("Invalid magic");

        ResHeaderNew32 res;
        if (meta.Version == 258 && meta.Architecture == ResourceArchitecture.Bits32 && meta.HeaderSize == sizeof(MetaHeader) + sizeof(ResHeaderNew32))
            res = ReadStruct<ResHeaderNew32>(stream);
        else if (meta.Version == 2 && meta.Architecture == ResourceArchitecture.Bits16 && meta.HeaderSize == sizeof(MetaHeader) + sizeof(ResHeaderOld16))
            res = (ResHeaderNew32)ReadStruct<ResHeaderOld16>(stream);
        else if (meta.Version == 2 && meta.Architecture == ResourceArchitecture.Grail2 && meta.HeaderSize == sizeof(MetaHeader) + sizeof(ResHeaderGrail2))
            res = (ResHeaderNew32)ReadStruct<ResHeaderGrail2>(stream);
        else
            throw new NotSupportedException($"Unsupported resource file: version={meta.Version} arch={meta.Architecture} headerSize={meta.HeaderSize}");
        ReadOnlySpan<OffsetSize> keyResources = MemoryMarshal.Cast<ulong, OffsetSize>(new ReadOnlySpan<ulong>(res.KeyResources, 15));

        Architecture = meta.Architecture;
        Version = meta.Version;
        (Title, SubTitle) = ExtractTitles(meta);
        EntryId = res.EntryId;
        MaxFadeColors = res.MaxFadeColors;
        MaxTransColors = res.MaxTransColors;
        DynamicResources = res.DynamicResources;
        CountStrings = res.CountStrings;
        CountVariables = res.CountVariables;
        MaxScrMsg = res.MaxScrMsg;
        BuildType = res.BuildType;
        Resources = ReadStructs<Resource>(stream, keyResources[(int)KeyResourceID.Resources]);
        Entries = ReadUnknownStructs(stream, keyResources[(int)KeyResourceID.Entries], 80, 82);
        IndexBuffers = ReadUnknownStructs(stream, keyResources[(int)KeyResourceID.IndexBuffers], 28, 44);
        Variables = ReadVariables(stream, keyResources[(int)KeyResourceID.Variables]);
        ConstStrings = ReadStrings(stream, keyResources[(int)KeyResourceID.ConstStrings]);
        ScriptSections = ReadScripts(stream, keyResources[(int)KeyResourceID.Scripts], res);
        Palette = ReadPalette(stream, keyResources[(int)KeyResourceID.Palette]);
        NameTable = ReadStrings(stream, keyResources[(int)KeyResourceID.NameTable]);
        Plugins = ReadPlugins(stream, keyResources);
        SourceFile = ReadStrings(stream, keyResources[(int)KeyResourceID.SourceFile]).FirstOrDefault() ?? "";

        UnknownKeyResource8 = ReadUnknown(stream, keyResources[(int)KeyResourceID.Unknown8]);
        UnknownKeyResource9 = ReadUnknown(stream, keyResources[(int)KeyResourceID.Unknown9]);
        UnknownKeyResource13 = ReadUnknown(stream, keyResources[(int)KeyResourceID.Unknown13]);

        extensionBasePath = Path.Join(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));

        PluginProcs = Plugins
            .SelectMany(p => Enumerable
                .Range(0, p.Procs.Count)
                .Select(i => (p, i)))
            .ToArray();
    }

    private (string title, string subTitle) ExtractTitles(in MetaHeader meta)
    {
        fixed (byte* data = meta.Title)
        {
            var title = Marshal.PtrToStringAnsi((nint)(data + 1))!;
            var offset = title.Length + 2;
            var subTitle = offset < 0x50
                ? Marshal.PtrToStringAnsi((nint)(data + offset))!
                : "";
            return (title, subTitle);
        }
    }

    private static T ReadStruct<T>(Stream stream) where T : unmanaged
    {
        T result = default;
        stream.ReadExactly(MemoryMarshal.AsBytes(new Span<T>(ref result)));
        return result;
    }

    private static T[] ReadStructs<T>(Stream stream, OffsetSize range) where T : unmanaged
    {
        if (range.Size == 0)
            return Array.Empty<T>();
        if (range.Offset + range.Size > stream.Length)
            throw new InvalidDataException("Invalid key resource range");
        if (range.Size % sizeof(T) != 0)
            throw new InvalidDataException("Key resource is not aligned to expected content size");
        var result = new T[range.Size / sizeof(T)];
        stream.Position = range.Offset;
        stream.ReadExactly(MemoryMarshal.AsBytes<T>(result));
        return result;
    }

    private static IReadOnlyList<string> ReadStrings(Stream stream, OffsetSize range)
    {
        var data = ReadUnknown(stream, range);
        if (data.Length == 0)
            return Array.Empty<string>();

        var result = new List<string>();
        for (int startI = 0; startI < data.Length;)
        {
            var endI = Array.IndexOf(data, (byte)0, startI);
            if (endI < 0)
                throw new InvalidDataException("String in key resource has no terminator");
            result.Add(Encoding.UTF8.GetString(data, startI, endI - startI));
            startI = endI + 1;
        }
        return result;
    }

    private byte[][] ReadUnknownStructs(Stream stream, OffsetSize range, int sizePerElement16, int sizePerElement32, bool allowUnaligned = false)
    {
        var sizePerElement = Architecture == ResourceArchitecture.Bits32 ? sizePerElement32 : sizePerElement16;

        if (range.Size == 0)
            return Array.Empty<byte[]>();
        if (range.Offset + range.Size > stream.Length)
            throw new InvalidDataException("Invalid key resource range");
        if (!allowUnaligned && range.Size % sizePerElement != 0)
            throw new InvalidDataException("Key resource is not aligned to expected content size");

        var result = new byte[range.Size / sizePerElement][];
        stream.Position = range.Offset;
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new byte[sizePerElement];
            stream.ReadExactly(result[i].AsSpan());
        }
        return result;
    }

    private KeyValuePair<uint, uint>[] ReadVariables(Stream stream, OffsetSize range)
    {
        uint[] values;
        if (Architecture != ResourceArchitecture.Bits32)
        {
            var shortValues = ReadStructs<ushort>(stream, range);
            values = shortValues.Select(i => (uint)i).ToArray();
        }
        else
            values = ReadStructs<uint>(stream, range);

        if (values.Length % 2 != 0)
            throw new InvalidDataException("Variable section does not align to content size");
        return Enumerable
            .Range(0, values.Length / 2)
            .Select(i => new KeyValuePair<uint, uint>(values[i * 2 + 0], values[i * 2 + 1]))
            .ToArray();
    }

    private static byte[][] ReadScripts(Stream stream, OffsetSize range, in ResHeaderNew32 res)
    {
        if (res.ScriptCount > 0x30)
            throw new InvalidDataException("Too many script sections");

        var result = new byte[res.ScriptCount][];
        stream.Position = range.Offset;
        var startOffset = 0u;
        for (int i = 0; i < res.ScriptCount; i++)
        {
            if (res.ScriptEndOffsets[i] <= startOffset)
                throw new InvalidDataException("Invalid script section size");
            var size = res.ScriptEndOffsets[i] - startOffset;
            result[i] = new byte[size];
            stream.ReadExactly(result[i].AsSpan());
            startOffset = res.ScriptEndOffsets[i];
        }
        if (startOffset != range.Size)
            throw new InvalidDataException("Unexpected extra data in script key resource");
        return result;
    }

    private Plugin[] ReadPlugins(Stream stream, ReadOnlySpan<OffsetSize> keyResources)
    {
        var plugins = ReadStrings(stream, keyResources[(int)KeyResourceID.Plugins]);
        var pluginProcs = ReadStrings(stream, keyResources[(int)KeyResourceID.PluginProcs]);
        var indexRange = keyResources[(int)KeyResourceID.PluginIndexPerProc];
        var pluginIndexPerProc = Architecture != ResourceArchitecture.Bits32
            ? ReadStructs<ushort>(stream, indexRange).Select(i => (uint)i).ToArray()
            : ReadStructs<uint>(stream, indexRange);

        if (pluginProcs.Count != pluginIndexPerProc.Length || pluginIndexPerProc.Any(i => i >= plugins.Count))
            throw new InvalidDataException("Invalid sizes of plugin key resources");
        var procsPerIndex = pluginProcs
            .Zip(pluginIndexPerProc)
            .ToLookup(t => t.Second, t => t.First);
        return plugins
            .Select((plugin, i) => new Plugin(plugin, procsPerIndex[(uint)i].ToArray()))
            .ToArray();
    }

    private static byte[] ReadUnknown(Stream stream, OffsetSize range)
    {
        if (range.Size == 0)
            return Array.Empty<byte>();
        if (range.Offset + range.Size > stream.Length)
            throw new InvalidDataException("Invalid key resource range");
        var data = new byte[range.Size];
        stream.Position = range.Offset;
        stream.ReadExactly(data.AsSpan());
        return data;
    }

    private static Rgba32[] ReadPalette(Stream stream, OffsetSize range)
    {
        var bytes = ReadUnknown(stream, range);
        if (bytes.Length % 4 != 0)
            throw new InvalidDataException("Palette key resource does not align with expected content size");

        return Enumerable
            .Range(0, bytes.Length / 4)
            .Select(i => new Rgba32(bytes[i * 4 + 0], bytes[i * 4 + 1], bytes[i * 4 + 2], 255))
            .ToArray();
    }

    private FileStream OpenResourceStream(byte extension)
    {
        if (Version == 2)
            return new FileStream(extensionBasePath + ".bin", FileMode.Open, FileAccess.Read);
        else
            return new FileStream($"{extensionBasePath}.{extension:D3}", FileMode.Open, FileAccess.Read);
    }

    public byte[] ReadResource(Resource resource)
    {
        if (resource.Type >= ResourceType.Movie && resource.Type <= ResourceType.Tile)
            return ReadScriptResource(resource);

        using var stream = OpenResourceStream(resource.Extension);
        if (resource.Offset + resource.Size > stream.Length)
            throw new InvalidDataException("Invalid resource range");
        var result = new byte[resource.Size];
        stream.Position = resource.Offset;
        stream.ReadExactly(result.AsSpan());
        return result;
    }

    private byte[] ReadScriptResource(Resource resource)
    {
        var curOffset = 0;
        foreach (var scriptSection in ScriptSections)
        {
            var resourceEnd = resource.Offset + resource.Size;
            if (curOffset <= resource.Offset && curOffset + scriptSection.Length >= resourceEnd)
                return scriptSection[(int)(resource.Offset - curOffset)..(int)(resourceEnd - curOffset)];

            curOffset += scriptSection.Length;
            if (curOffset > resource.Offset)
                throw new InvalidDataException("Script resource does not fit into single section");
        }
        throw new ArgumentOutOfRangeException(nameof(resource), "Script resource is out of bounds");
    }
}
