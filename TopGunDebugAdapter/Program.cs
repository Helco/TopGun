using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;

namespace TopGun.DebugAdapter;

internal class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Debug()
            .MinimumLevel.Verbose()
            .CreateLogger();
        var loggerFactory = LoggerFactory.Create(config => config.AddSerilog());

        var cancel = CancellationToken.None;
        using var client = new ScummVMConsoleClient(loggerFactory.CreateLogger<ScummVMConsoleClient>());
        client.OnMessage += msg => Console.WriteLine(string.Join("\n", msg.Prepend("Got free message: ").Append("\n")));
        client.OnError += err => Console.WriteLine("Got error: " + err);
        await client.Connect("127.0.0.1", 2346, cancel);

        while(true)
        {
            var command = Console.ReadLine();
            if (command == null)
                break;
            var response = await client.SendCommand(command, cancel);
            Console.WriteLine(string.Join("\n", response.Prepend("Response:")));
        }
    }
}