﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using TopGun;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Net.WebSockets;

namespace TopGunTool;

internal class Program
{
    static void Main(string[] args) => MainPrintObjects(args);

    private TextWriter output = null!;
    private ResourceFile resFile = null!;

    private void WriteFullResource(uint index)
    {
        var resource = resFile.Resources[(int)index];
        var data = resFile.ReadResource(resource);
        output.Write(resource.Type);
        output.Write(" - ");
        output.WriteLine(index);
        switch(resource.Type)
        {
            case ResourceType.Cell:
                output.Write("  - ");
                WriteCellContent(data);
                output.WriteLine();
                break;
            case ResourceType.Group:
                var group = new Group(data);
                WriteResourceList(group.Children);
                break;
            case ResourceType.Queue:
                var queueReader = new SpanReader(data);
                while (!queueReader.EndOfSpan)
                {
                    var msg = new SpriteMessage(ref queueReader);
                    output.WriteLine(msg.ToStringWithoutData());
                    if (!msg.Data.IsEmpty)
                        WriteScriptContent(msg.Data);
                }
                break;
            case ResourceType.Script: WriteScriptContent(data); break;
            case ResourceType.Sprite:
                var sprite = new Sprite(data);
                output.Write(sprite.ToStringWithoutResources());
                output.WriteLine("Resources: ");
                WriteResourceList(sprite.Resources);
                break;
        }
    }

    private void WriteResourceList(IReadOnlyList<uint> list)
    {
        foreach (var index in list)
        {
            output.Write("    - ");
            WriteShortResource(index);
            output.WriteLine();
        }
    }

    private void WriteShortResource(uint index)
    {
        var resource = resFile.Resources[(int)index];
        output.Write(resource.Type);
        output.Write(" - ");
        output.Write(index);
        if (resource.Type == ResourceType.Cell)
        {
            output.Write(" (");
            WriteCellContent(resFile.ReadResource(resource));
            output.Write(')');
        }
    }

    private void WriteCellContent(ReadOnlySpan<byte> data)
    {
        var cell = new Cell(data);
        WriteShortResource(cell.Bitmap);
        output.Write(" @ ");
        output.Write(cell.OffsetX);
        output.Write(',');
        output.Write(cell.OffsetY);
    }

    private void WriteScriptContent(ReadOnlySpan<byte> data)
    {
        var script = new SpanReader(data);
        while (!script.EndOfSpan)
        {
            var rootInstr = new ScriptRootInstruction(ref script);
            output.WriteLine(rootInstr.ToStringWithoutData());
            if (rootInstr.Data.IsEmpty)
                continue;

            var calcScript = new SpanReader(rootInstr.Data);
            while (!calcScript.EndOfSpan)
            {
                output.Write('\t');
                output.WriteLine(new ScriptCalcInstruction(ref calcScript));
            }
        }
    }

    private void WriteConstStrings()
    {
        if (resFile.ConstStrings.Count == 0)
            return;
        output.WriteLine("Const strings");
        foreach (var (offset, value) in resFile.ConstStrings)
            output.WriteLine($"{offset | 0x8000:D5}: {value}");
    }

    static void MainPrintObjects(string[] args)
    {
        var allPaths = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        var allResFiles = new List<ResourceFile>();
        foreach (var resFilePath in allPaths)
        {
            if (!resFilePath.Contains("tama"))
                continue;

            var resourceFile = new ResourceFile(resFilePath);
            using var objectOutput = new StreamWriter(resFilePath + ".objects.txt");
            //var objectOutput = Console.Out;
            var writer = new Program();
            writer.output = objectOutput;
            writer.resFile = resourceFile;
            writer.WriteConstStrings();
            foreach (var (index, res) in resourceFile.Resources.Select((r, i) => (i, r)))
            {
                if (res.Type == ResourceType.Script || res.Type == ResourceType.Queue)
                    continue;

                objectOutput.WriteLine();
                objectOutput.WriteLine();
                writer.WriteFullResource((uint)index);
            }
        }
    }

    static void MainPrintQueues(string[] args)
    {
        var allPaths = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        var allResFiles = new List<ResourceFile>();
        foreach (var resFilePath in allPaths)
        {
            if (!resFilePath.Contains("tama"))
                continue;

            var resourceFile = new ResourceFile(resFilePath);
            using var queueOutput = new StreamWriter(resFilePath + ".queues.txt");
            //var queueOutput = Console.Out;
            foreach (var (index, res) in resourceFile.Resources.Select((r, i) => (i, r)).Where(t => t.r.Type == ResourceType.Queue))
            {
                var queueFull = resourceFile.ReadResource(res);

                queueOutput.WriteLine();
                queueOutput.WriteLine();
                queueOutput.WriteLine($"{Path.GetFileNameWithoutExtension(resFilePath)} - {index}");

                var queueReader = new SpanReader(queueFull.AsSpan());
                while (!queueReader.EndOfSpan)
                {
                    var msg = new SpriteMessage(ref queueReader);
                    queueOutput.WriteLine(msg.ToStringWithoutData());
                    if (!msg.Data.IsEmpty)
                    {
                        var decompiler = new ScriptDecompiler(index, msg.Data, resourceFile);
                        decompiler.Decompile();
                        decompiler.WriteTo(queueOutput, 1);
                    }
                }
            }
        }
    }

    static void MainDecompileScripts(string[] args)
    {
        var allPaths = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        var allResFiles = new List<ResourceFile>();
        var scriptCount = 0;
        foreach (var resFilePath in allPaths)
        {
            if (!resFilePath.Contains("tama"))
                continue;

            var resourceFile = new ResourceFile(resFilePath);
            using var scriptOutput = new CodeWriter(new StreamWriter(resFilePath + ".scripts.txt"));
            var scriptDebugInfos = new SortedDictionary<int, ScriptDebugInfo>();
            SymbolMap? symbolMap = null;
            if (File.Exists(resFilePath + ".symbols.json"))
                symbolMap = JsonSerializer.Deserialize<SymbolMap>(File.ReadAllText(resFilePath + ".symbols.json"));

            foreach (var (key, value) in resourceFile.Variables.OrderBy(v => v.Key))
            {
                string keyName;
                if (key < 5001)
                    keyName = symbolMap?.SceneVariables.GetValueOrDefault((int)key) ?? $"scene{key}";
                else
                    keyName = symbolMap?.SystemVariables.GetValueOrDefault((int)key) ?? $"system{key}";
                scriptOutput.WriteLine($"&{keyName} = {unchecked((int)value)};");
            }

            foreach (var (index, res) in resourceFile.Resources.Select((r, i) => (i, r)).Where(t => t.r.Type == ResourceType.Script))
            {
                var scriptFull = resourceFile.ReadResource(res);

                scriptOutput.WriteLine();
                scriptOutput.WriteLine();
                scriptOutput.Write($"{Path.GetFileNameWithoutExtension(resFilePath)} - {index}");
                if (symbolMap?.Scripts?.TryGetValue(index, out var scriptSymbolMap) == true && scriptSymbolMap.Name != null)
                    scriptOutput.WriteLine($" - {scriptSymbolMap.Name}");
                else
                    scriptOutput.WriteLine();

                var decompiler = new ScriptDecompiler(index, scriptFull, resourceFile);
                decompiler.Decompile();
                if (symbolMap != null)
                    decompiler.ApplySymbolMap(symbolMap);
                decompiler.WriteTo(scriptOutput);
                scriptDebugInfos.Add(index, decompiler.CreateDebugInfo());
                
                scriptCount++;
            }
            var sceneDebugInfo = new SceneDebugInfo(scriptDebugInfos);
            File.WriteAllText(resFilePath + ".debug.json", JsonSerializer.Serialize(sceneDebugInfo));
        }

        Console.WriteLine($"Decompiled {scriptCount} scripts");
    }

    static void MainPrintScripts(string[] args)
    {
        var allPaths = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        var allResFiles = new List<ResourceFile>();
        foreach (var resFilePath in allPaths)
        {
            if (!resFilePath.Contains("tama"))
                continue;

            var resourceFile = new ResourceFile(resFilePath);
            using var scriptOutput = new StreamWriter(resFilePath + ".disassembly.txt");
            foreach (var (index, res) in resourceFile.Resources.Select((r, i) => (i, r)).Where(t => t.r.Type == ResourceType.Script))
            {
                var scriptFull = resourceFile.ReadResource(res);

                scriptOutput.WriteLine();
                scriptOutput.WriteLine();
                scriptOutput.WriteLine($"{Path.GetFileNameWithoutExtension(resFilePath)} - {index}");

                var script = new SpanReader(scriptFull.AsSpan());
                while (!script.EndOfSpan)
                {
                    var rootInstr = new ScriptRootInstruction(ref script);
                    scriptOutput.WriteLine(rootInstr.ToStringWithoutData());
                    if (rootInstr.Data.IsEmpty)
                        continue;

                    var calcScript = new SpanReader(rootInstr.Data);
                    while (!calcScript.EndOfSpan)
                    {
                        scriptOutput.Write('\t');
                        scriptOutput.WriteLine(new ScriptCalcInstruction(ref calcScript, rootInstr.DataOffset));
                    }
                }
            }
        }
    }

    static void MainConvertSingle(string[] args)
    {
        var resFile = new ResourceFile(@"C:\dev\TopGun\games\tama\TAMA.bin");
        var data = File.ReadAllBytes(@"C:\dev\TopGun\games\tama\TAMA\142.tgbitmap");
        var bitmap = new Bitmap(data);
        bitmap.Expand();

        var pixels = Image.LoadPixelData(bitmap.Data.ToArray().Select(i => (i < resFile.Palette.Count ? resFile.Palette[i] : new Rgba32(255, 0, 255))).ToArray(), bitmap.AlignedWidth, bitmap.Height);
        pixels.SaveAsPng(@"C:\dev\TopGun\games\tama\TAMA_out\142.png");
    }

    private static Rgba32[] LowColors =
    {
        new Rgba32(0, 0, 0),
        new Rgba32(128, 0, 0),
        new Rgba32(0, 128, 0),
        new Rgba32(128, 128, 0),
        new Rgba32(0, 0, 128),
        new Rgba32(128, 0, 128),
        new Rgba32(0, 128, 128),
        new Rgba32(192, 192, 192),
        new Rgba32(192, 220, 192),
        new Rgba32(166, 202, 240)
    };

    private static Rgba32[] HighColors =
    {
       new Rgba32(255, 251, 240),
        new Rgba32(160, 160, 164),
        new Rgba32(128, 128, 128),
        new Rgba32(255, 0, 0),
        new Rgba32(0, 255, 0),
        new Rgba32(255, 255, 0),
        new Rgba32(0, 0, 255),
        new Rgba32(255, 0, 255),
        new Rgba32(0, 255, 255),
        new Rgba32(255, 255, 255)
    };

    static void MainConvert(string[] args)
    {
        var allPaths = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        var allResFiles = new List<ResourceFile>();
        foreach (var resFilePath in allPaths)
        {
            if (!resFilePath.Contains("tama"))
                continue;
            Console.Write(resFilePath + "...");
            try
            {
                var resourceFile = new ResourceFile(resFilePath);

                var bitmapPath = Path.Join(Path.GetDirectoryName(resFilePath), Path.GetFileNameWithoutExtension(resFilePath) + "_out");
                Directory.CreateDirectory(bitmapPath);
                foreach (var (index, res) in resourceFile.Resources.Select((r, i) => (i, r)).Where(t => t.r.Type == ResourceType.Bitmap))
                {
                    var bitmap = new Bitmap(resourceFile.ReadResource(res));
                    bitmap.Expand();
                    var pixels = Image.LoadPixelData(bitmap.Data.ToArray().Select(i =>
                    {
                        if (i == 0)
                            return new Rgba32(255, 0, 255);
                        else if (i < 10)
                            return LowColors[i+1000];
                        else if (i - 10 < resourceFile.Palette.Count)
                            return resourceFile.Palette[i - 10];
                        else
                            return HighColors[i - 246+1000];
                    }).ToArray(), bitmap.AlignedWidth, bitmap.Height);
                    pixels.SaveAsPng(Path.Join(bitmapPath, $"{index}.png"));
                }

                Console.WriteLine($"done. \"{resourceFile.Title}\" - \"{resourceFile.SubTitle}\"");
                allResFiles.Add(resourceFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }

    static void MainExtract(string[] args)
    {
        var allPaths = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        var allResFiles = new List<ResourceFile>();
        foreach (var resFilePath in allPaths)
        {
            Console.Write(resFilePath + "...");
            try
            {
                var resourceFile = new ResourceFile(resFilePath);

                var bitmapPath = Path.Join(Path.GetDirectoryName(resFilePath), Path.GetFileNameWithoutExtension(resFilePath));
                Directory.CreateDirectory(bitmapPath);
                foreach (var (index, res) in resourceFile.Resources.Select((r, i) => (i, r)).Where(t => t.r.Type == ResourceType.Bitmap))
                {
                    var data = resourceFile.ReadResource(res);
                    File.WriteAllBytes(Path.Join(bitmapPath, $"{index}.tgbitmap"), data);
                }

                Console.WriteLine($"done. \"{resourceFile.Title}\" - \"{resourceFile.SubTitle}\"");
                allResFiles.Add(resourceFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Total resources: " + allResFiles.Sum(r => r.Resources.Count));
        foreach (var resType in Enum.GetValues<ResourceType>())
            Console.WriteLine($"Total {resType}: " + allResFiles.Sum(r => r.Resources.Count(r => r.Type == resType)));
    }
}