using System;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Server;
using TopGun.DebugAdapter.Handlers;

namespace TopGun.DebugAdapter;

internal class DebugAdapterOptions
{
    public FileInfo ResourceDir { get; set; } = null!;
    public string EngineHost { get; set; } = "127.0.0.1";
    public ushort EnginePort { get; set; } = 2346;
    public bool MergeRootCalcFrames { get; set; } = true;
    public bool StopOnEntry { get; set; }
    public bool WaitForDebugger { get; set; }
    public bool Verbose { get; set; }
}

internal class Program
{
    static async Task Main(string[] args)
    {
        var defaultOpts = new DebugAdapterOptions();
        var resourceDirOption = new Option<FileInfo>("--resourceDir", "The path to the directory containing the resource and decompiled/disassembled script files")
            .LegalFilePathsOnly();
        var engineHostOption = new Option<string>("--engineHost", "The IP address or host name of the running TopGun engine in ScummVM");
        var enginePortOption = new Option<ushort>("--enginePort", "The port of the running TopGun engine in ScummVM");
        var mergeRootCalcFramesOption = new Option<bool>("--mergeRootCalcFrames", "Whether root-calc stackframes should be merged together");
        var stopOnEntryOption = new Option<bool>("--stopOnEntry", "Stops the game upon attaching");
        var waitForDebuggerOption = new Option<bool>("--waitForDebugger", "Wait for a debugger to attach before starting the debug adapter server");
        var verboseOption = new Option<bool>("--verbose", "Verbose logging output");
        resourceDirOption.IsRequired = true;
        engineHostOption.SetDefaultValue(defaultOpts.EngineHost);
        enginePortOption.SetDefaultValue(defaultOpts.EnginePort);
        mergeRootCalcFramesOption.SetDefaultValue(defaultOpts.MergeRootCalcFrames);
        stopOnEntryOption.SetDefaultValue(true);

        var rootCommand = new RootCommand("TopGun in ScummVM debug adapter server")
        {
            resourceDirOption,
            engineHostOption,
            enginePortOption,
            mergeRootCalcFramesOption,
            stopOnEntryOption,
            waitForDebuggerOption,
            verboseOption
        };

        rootCommand.TreatUnmatchedTokensAsErrors = true;
        rootCommand.Handler = CommandHandler.Create<DebugAdapterOptions>(HandleRootCommand);
        await rootCommand.InvokeAsync(args);
    }

    private static async Task HandleRootCommand(DebugAdapterOptions opts)
    {
        if (opts.WaitForDebugger)
        {
            Debug.WriteLine("Waiting for .NET debugger...");
            while (!Debugger.IsAttached) { await Task.Delay(1000); }
        }

        var logLevel = opts.Verbose ? LogLevel.Trace : LogLevel.Information;

        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddSingleton(opts)
            .AddSingleton<ScummVMConsoleClient>()
            .AddSingleton<ScummVMConsoleAPI>()
            .AddSingleton<SceneInfoLoader>()
            .AddTransient(typeof(Lazy<>), typeof(Lazier<>))
            .AddLogging(logConfig => logConfig
                .AddDebug()
                .SetMinimumLevel(logLevel))
            .AddDebugAdapterServer(config => config
                .AddDefaultLoggingProvider()
                .ConfigureLogging(logConfig => logConfig
                    .AddDebug()
                    .SetMinimumLevel(logLevel))
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .WithHandler<DisconnectHandler>()
                .WithHandler<AttachHandler>()
                .WithHandler<ThreadsHandler>()
                .WithHandler<ContinueHandler>()
                .WithHandler<PauseHandler>()
                .WithHandler<StackTraceHandler>()
                .WithHandler<NextHandler>()
                .WithHandler<StepInHandler>()
                .WithHandler<StepOutHandler>()
                .WithHandler<SetBreakpointsHandler>()
                .WithHandler<EvaluateHandler>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, LogToDebugOutputProvider>());

        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        var host = builder.Build();

        var debugAdapterServer = host.Services.GetRequiredService<DebugAdapterServer>();
        var logger = host.Services.GetRequiredService<ILogger<DebugAdapterServer>>();
        var api = host.Services.GetRequiredService<ScummVMConsoleAPI>();

        api.AddAlwaysMessageHandler(message =>
        {
            logger.LogWarning("Got unexpected or unknown message: {message}", message);
            return true;
        });

        await debugAdapterServer.Initialize(CancellationToken.None);
        host.Services.GetServices<ILoggerProvider>().OfType<LogToDebugOutputProvider>().Single().Server = debugAdapterServer;
        logger.LogInformation("Initialized");

        await host.RunAsync();
    }
}

internal class Lazier<T> : Lazy<T> where T : class
{
    public Lazier(IServiceProvider provider)
        : base(() => provider.GetRequiredService<T>())
    {
    }
}

internal class LogToDebugOutputProvider : ILoggerProvider
{
    public DebugAdapterServer? Server { get; set; }

    public LogToDebugOutputProvider()
    {
    }

    public ILogger CreateLogger(string categoryName) => new LogToDebugOutput(this, categoryName);

    public void Dispose() { }
}

internal class LogToDebugOutput : ILogger
{
    private readonly LogToDebugOutputProvider provider;
    private readonly string categoryName;

    public LogToDebugOutput(LogToDebugOutputProvider provider, string categoryName)
    {
        this.provider = provider;
        this.categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        provider.Server?.SendOutput(new OutputEvent()
        {
            Category = OutputEventCategory.Console,
            Output = $"{logLevel} - {categoryName}: {formatter(state, exception)}\n"
        });
    }
}
