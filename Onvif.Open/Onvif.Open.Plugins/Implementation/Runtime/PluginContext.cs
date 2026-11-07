// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Plugins.Implementation.Runtime
{
    internal sealed class PluginContext : IPluginContext
    {
        #region Constructor

        public PluginContext(string pluginName, IReadOnlyDictionary<string, string> settings, ILogger logger, TimeProvider timeProvider, IPluginCameras cameras, Func<string, PluginEvent, CancellationToken, Task> publish)
        {
            PluginName = pluginName;
            Settings = settings;
            Logger = logger;
            TimeProvider = timeProvider;
            Cameras = cameras;
            _publish = publish;
        }

        #endregion

        #region Private Variables

        private readonly Func<string, PluginEvent, CancellationToken, Task> _publish;

        #endregion

        #region Public Variables

        public string PluginName { get; }
        public IReadOnlyDictionary<string, string> Settings { get; }
        public ILogger Logger { get; }
        public TimeProvider TimeProvider { get; }
        public IPluginCameras Cameras { get; }

        #endregion

        #region Public Methods

        public Task PublishAsync(PluginEvent pluginEvent, CancellationToken cancellationToken = default)
        {
            if (pluginEvent == null)
            {
                throw new ArgumentNullException(nameof(pluginEvent));
            }

            if (pluginEvent.CameraId != null && !Cameras.IsAssigned(pluginEvent.CameraId))
            {
                throw new UnauthorizedAccessException($"Camera '{pluginEvent.CameraId}' is not assigned to plugin '{PluginName}'.");
            }

            return _publish(PluginName, pluginEvent, cancellationToken);
        }

        #endregion
    }
}
