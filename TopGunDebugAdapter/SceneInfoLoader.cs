using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace TopGun.DebugAdapter;

internal class SceneInfoLoader
{
    private readonly DebugAdapterOptions options;
    private readonly ScummVMConsoleAPI api;
    private readonly Lazy<DebugAdapterServer> server;
    private readonly ILogger<SceneInfoLoader> logger;
    private readonly Dictionary<string, SceneInfo> sceneInfos = new();
    private readonly SemaphoreSlim sceneInfosSemaphore = new(1, 1);

    public DebugAdapterServer Server => server.Value;

    public SceneInfoLoader(DebugAdapterOptions options, ScummVMConsoleAPI api, Lazy<DebugAdapterServer> server, ILogger<SceneInfoLoader> logger)
    {
        this.options = options;
        this.api = api;
        this.server = server;
        this.logger = logger;
    }

    public async Task<SceneInfo> LoadCurrentSceneInfo(CancellationToken cancel)
    {
        var (sceneStack, curSceneI) = await api.SceneStack(cancel);
        await Task.WhenAll(sceneStack.Distinct().Select(name => LoadSceneInfo(name, cancel)));
        return sceneInfos[sceneStack[curSceneI ?? 0]];
    }

    public async Task<SceneInfo?> TryLoadSceneInfoAtPath(string path, CancellationToken cancel)
    {
        var sceneInfo = sceneInfos.Values.FirstOrDefault(i => i.Decompiled?.Path?.Equals(path, StringComparison.OrdinalIgnoreCase) == true);
        if (sceneInfo != null)
            return sceneInfo;
        if (!path.EndsWith(".scripts.txt") || !path.StartsWith(options.ResourceDir.FullName))
            return sceneInfo;

        var name = Path.GetFileName(path)[..^(".scripts.txt".Length)];
        await LoadSceneInfo(name, cancel);
        return sceneInfos[name];
    }

    private async Task LoadSceneInfo(string name, CancellationToken cancel)
    {
        var basePath = Path.Combine(options.ResourceDir.FullName, name);
        var errorMessages = new List<string>();
        var missingFiles = new List<string>();

        ResourceFile? resourceFile = null;
        if (File.Exists(basePath))
        {
            try
            {
                resourceFile = new ResourceFile(basePath);
            }
            catch (Exception e) { errorMessages.Add("Error loading resource file: " + e.Message); }
        }

        var decompiled = await TryLoadSource(basePath + ".scripts.txt", errorMessages, cancel);
        var disassembly = await TryLoadSource(basePath + ".disassembly.txt", errorMessages, cancel);
        var debugInfo = await TryLoadJson<SceneDebugInfo?>(basePath + ".debug.json", errorMessages, cancel);
        var symbolMap = await TryLoadJson<SymbolMap?>(basePath + ".symbols.json", errorMessages, cancel);

        if (errorMessages.Any())
        {
            var server = this.server.Value;
            server.SendOutput(new()
            {
                Category = OutputEventCategory.Console,
                Group = OutputEventGroup.StartCollapsed,
                Output = "Errors during scene info loading for " + name
            });
            foreach (var message in errorMessages)
            {
                server.SendOutput(new()
                {
                    Category = OutputEventCategory.Console,
                    Output = message
                });
            }
            server.SendOutput(new()
            {
                Category = OutputEventCategory.Console,
                Group = OutputEventGroup.End
            });
        }

        if (resourceFile == null) missingFiles.Add("resource file");
        if (decompiled == null) missingFiles.Add("decompiled scripts");
        if (disassembly == null) missingFiles.Add("disassembled scripts");
        if (debugInfo == null) missingFiles.Add("debug info");
        if (symbolMap == null) missingFiles.Add("symbol map");
        if (missingFiles.Any())
            logger.LogWarning("Scene info for {sceneName} is missing files: {missingFiles}", name, missingFiles);

        await sceneInfosSemaphore.WaitAsync(cancel);
        try
        {
            sceneInfos[name] = new()
            {
                Name = Path.GetFileNameWithoutExtension(name),
                ResourceFile = resourceFile,
                Decompiled = decompiled,
                Disassembly = disassembly,
                DebugInfo = debugInfo,
                SymbolMap = symbolMap
            };
        }
        finally
        {
            sceneInfosSemaphore.Release();
        }
    }

    private async Task<Source?> TryLoadSource(string path, List<string> errorMessages, CancellationToken cancel)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var content = await File.ReadAllBytesAsync(path, cancel);
            var md5 = MD5.HashData(content);
            var sha1 = SHA1.HashData(content);
            var sha256 = SHA256.HashData(content);
            return new Source()
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path,
                PresentationHint = SourcePresentationHint.Emphasize,
                Checksums = new[]
                {
                        new Checksum()
                        {
                            Algorithm = ChecksumAlgorithm.Md5,
                            Value = Convert.ToHexString(md5)
                        },
                        new Checksum()
                        {
                            Algorithm = ChecksumAlgorithm.Sha1,
                            Value = Convert.ToHexString(sha1)
                        },
                        new Checksum()
                        {
                            Algorithm = ChecksumAlgorithm.Sha256,
                            Value = Convert.ToHexString(sha256)
                        }
                }
            };
        }
        catch (Exception e)
        {
            errorMessages.Add($"Error loading source \"{Path.GetFileName(path)}\": " + e.Message);
        }
        return null;
    }

    private async Task<T> TryLoadJson<T>(string path, List<string> errorMessages, CancellationToken cancel)
    {
        if (!File.Exists(path))
            return default!;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancel);
            return JsonSerializer.Deserialize<T>(json)!;
        }
        catch (Exception e)
        {
            errorMessages.Add($"Error loading JSON \"{Path.GetFileName(path)}\": " + e.Message);
        }
        return default!;
    }
}
