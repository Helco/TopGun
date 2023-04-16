using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TopGun;

namespace TopGunTool;

internal class Program
{
    static void Main(string[] args)
    {
        var allResourceFiles = Directory.GetFiles(@"C:\dev\TopGun\games", "*.bin", SearchOption.AllDirectories);
        foreach (var resFilePath in allResourceFiles)
        {
            //if (resFilePath.Contains("grail")) continue;
            Console.Write(resFilePath + "...");
            try
            {
                var resourceFile = new ResourceFile(resFilePath);
                Console.WriteLine($"done. \"{resourceFile.Title}\" - \"{resourceFile.SubTitle}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}