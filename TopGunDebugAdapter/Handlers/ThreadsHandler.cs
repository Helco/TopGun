using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using Thread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace TopGun.DebugAdapter.Handlers;

internal class ThreadsHandler : BaseHandler<ThreadsHandler>, IThreadsHandler
{
    public ThreadsHandler(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public Task<ThreadsResponse> Handle(ThreadsArguments request, CancellationToken cancellationToken)
    {
        pauseService.SendPauseByCommand();
        return Task.FromResult(new ThreadsResponse()
        {
            Threads = new[]
            {
                new Thread()
                {
                    Id = 0,
                    Name = "Scripts"
                }
            }
        });
    }
}
