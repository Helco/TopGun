using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TopGun;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TopGunTool;

internal class Program
{
    static void Main(string[] args) => MainPrintScripts(args);

    static void MainPrintScripts(string[] args)
    {
        var allPaths = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        var allResFiles = new List<ResourceFile>();
        foreach (var resFilePath in allPaths)
        {
            if (!resFilePath.Contains("tama"))
                continue;

            var resourceFile = new ResourceFile(resFilePath);
            foreach (var (index, res) in resourceFile.Resources.Select((r, i) => (i, r)).Where(t => t.r.Type == ResourceType.Script))
            {
                var scriptFull = resourceFile.ReadResource(res);

                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(resFilePath)} - {index}");
                Console.WriteLine(Convert.ToHexString(scriptFull));

                ReadOnlySpan<byte> script = scriptFull.AsSpan();
                while (!script.IsEmpty)
                {
                    var rootInstr = new ScriptRootInstruction(ref script);
                    Console.WriteLine(rootInstr.ToStringWithoutData());
                    if (rootInstr.Data.IsEmpty)
                        continue;

                    var calcScript = rootInstr.Data;
                    while (!calcScript.IsEmpty)
                    {
                        Console.Write('\t');
                        Console.WriteLine(new ScriptCalcInstruction(ref calcScript));
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
                    if (!bitmap.IsCompressed)
                        continue;
                    bitmap.Expand();
                    var pixels = Image.LoadPixelData(bitmap.Data.ToArray().Select(i =>
                    {
                        if (i == 0)
                            return new Rgba32(255, 0, 255);
                        else if (i < 10)
                            throw new InvalidDataException("Unexpected color index in bitmap");
                        else if (i - 10 < resourceFile.Palette.Count)
                            return resourceFile.Palette[i - 10];
                        else
                            throw new InvalidDataException("Unexpected color index in bitmap");
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