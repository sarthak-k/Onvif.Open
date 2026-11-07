// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Plugins.Implementation.Builder;

namespace Onvif.Open.Plugins.Interface
{
    internal interface IPluginHostBuilder
    {
        #region Public Methods

        IPluginHost Build();
        PluginHostBuilder SetRecordingManager(IRecordingManager manager);
        PluginHostBuilder AddPlugin(IPlugin plugin, IEnumerable<string> cameras = null, IReadOnlyDictionary<string, string> settings = null);
        PluginHostBuilder SetSnapshotProvider(Func<string, CancellationToken, Task<byte[]>> snapshotProvider);
        PluginHostBuilder SetPtzProvider(Func<string, ICameraPtz> ptzProvider);
        PluginHostBuilder SetImagingProvider(Func<string, ICameraImaging> imagingProvider);
        PluginHostBuilder SetIoProvider(Func<string, ICameraIo> ioProvider);
        PluginHostBuilder SetFrameQueueCapacity(int capacity);
        PluginHostBuilder SetNotificationQueueCapacity(int capacity);
        PluginHostBuilder SetEventHistoryCapacity(int capacity);
        PluginHostBuilder SetStopTimeout(TimeSpan timeout);
        PluginHostBuilder SetLoggerFactory(ILoggerFactory loggerFactory);
        PluginHostBuilder SetTimeProvider(TimeProvider timeProvider);

        #endregion
    }
}
