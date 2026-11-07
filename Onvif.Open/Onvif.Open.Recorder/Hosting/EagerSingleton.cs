// Sarthak Killedar
// 2026-11-07

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Onvif.Open.Recorder.Hosting
{
    /// <summary>Resolves a singleton when the host starts instead of on first use.</summary>
    internal sealed class EagerSingleton<T> : IHostedService
    {
        private readonly IServiceProvider _services;

        public EagerSingleton(IServiceProvider services)
        {
            _services = services;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _services.GetRequiredService<T>();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
