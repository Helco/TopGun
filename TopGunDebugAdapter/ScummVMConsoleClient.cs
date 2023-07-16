using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TopGun.DebugAdapter;

internal class ScummVMConsoleClient : IDisposable
{
    private readonly TcpClient tcpClient = new()
    {
        ReceiveTimeout = 10000,
        SendTimeout = 10000
    };
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly TaskCompletionSource connectCompletion = new();
    private readonly CancellationTokenSource cancelIntervalRead = new();
    private readonly byte[] buffer = new byte[4096];
    public readonly ILogger<ScummVMConsoleClient> logger;
    private int bufferAvailable = 0;
    private Task? intervalReadTask;
    private bool disposedValue;

    public event Action<Exception>? OnError;
    public event Action<IReadOnlyList<string>>? OnMessage;

    public ScummVMConsoleClient(ILogger<ScummVMConsoleClient> logger) => this.logger = logger;

    public async Task Connect(string host, int port, CancellationToken cancel)
    {
        cancel.Register(() => connectCompletion.TrySetCanceled());
        try
        {
            await tcpClient.ConnectAsync(host, port, cancel);
            intervalReadTask = Task.Run(IntervalRead, cancelIntervalRead.Token);
            logger.LogInformation("Connected to ScummVM");
            connectCompletion.SetResult();
        }
        catch(Exception e)
        {
            connectCompletion.TrySetException(e);
            logger.LogError(e, "Exception during connect");
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> SendCommand(string command, CancellationToken cancel)
    {
        await connectCompletion.Task;
        await semaphore.WaitAsync(cancel);
        try
        {
            await FlushIncomingMessages();

            logger.LogInformation("Sending command: {command}", command);
            var stream = tcpClient.GetStream();
            var buffer = Encoding.UTF8.GetBytes(command + "\n");
            await stream.WriteAsync(buffer, cancel);
            return await ReadMessage(cancel);
        }
        catch(SocketException e) { DisconnectDueTo(e); throw; }
        catch(IOException e) { DisconnectDueTo(e); throw; }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<IReadOnlyList<string>> ReadMessage(CancellationToken cancel)
    {
        var message = new List<string>();
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            
            var line = await ReadLine(cancel);
            if (line == "BYE")
                throw new IOException("Server waved goodbye");
            else if (line == "EOM")
                return message.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            else
                message.Add(line);
        }
    }

    private async Task<string> ReadLine(CancellationToken cancel)
    {
        var stream = tcpClient.GetStream();
        while (true)
        {
            cancel.ThrowIfCancellationRequested();

            var endLineI = Array.IndexOf(buffer, (byte)'\n', 0, bufferAvailable);
            if (endLineI >= 0)
            {
                var line = Encoding.UTF8.GetString(buffer, 0, endLineI);
                if (endLineI + 1 < bufferAvailable)
                    Array.Copy(buffer, endLineI + 1, buffer, 0, bufferAvailable - endLineI - 1);
                bufferAvailable -= endLineI + 1;
                return line;
            }

            if (bufferAvailable >= buffer.Length)
                throw new IOException($"Line is longer than maximum ({buffer.Length})");
            bufferAvailable += await stream.ReadAsync(buffer.AsMemory(bufferAvailable), cancel);
        }
    }

    private async Task FlushIncomingMessages()
    {
        while (tcpClient.Available > 0)
        {
            var message = await ReadMessage(cancelIntervalRead.Token);
            OnMessage?.Invoke(message);
            logger.LogInformation("Got free message: {message}", message);
        }
    }

    private async Task IntervalRead()
    {
        while (!cancelIntervalRead.IsCancellationRequested)
        {
            if (await semaphore.WaitAsync(0))
            {
                try
                {
                    await FlushIncomingMessages();
                }
                catch (SocketException e)
                {
                    DisconnectDueTo(e);
                }
                finally
                {
                    semaphore.Release();
                }
            }
            await Task.Delay(33, cancelIntervalRead.Token);
        }
    }

    private void DisconnectDueTo(Exception e)
    {
        logger.LogError(e, "Disconnect from ScummVM due to exception");
        try { tcpClient.Dispose(); } catch(Exception) {}
        cancelIntervalRead.Cancel();
        OnError?.Invoke(e);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                cancelIntervalRead.Cancel();
                intervalReadTask?.Wait(1000);
                semaphore.Wait(1000);
                tcpClient.Dispose();
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
