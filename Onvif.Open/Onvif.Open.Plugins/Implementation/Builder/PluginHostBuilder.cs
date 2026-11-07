// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Plugins.Implementation.Runtime;
using Onvif.Open.Plugins.Interface;

namespace Onvif.Open.Plugins.Implementation.Builder
{
    /// <summary>
    /// Builds a <see cref="PluginHost"/>.
    /// <code>
    /// await using var plugins = PluginHostBuilder.GetBuilder()
    ///     .SetRecordingManager(recorder)
    ///     .AddPlugin(new MyExamProctoringPlugin(), cameras: new[] { "room-101" })
    ///     .AddPlugins(PluginLoader.LoadFromFolder("plugins/MyAlerts"))
    ///     .Build();
    /// await plugins.StartAsync();
    /// </code>
    /// </summary>
    public sealed class PluginHostBuilder : IPluginHostBuilder
    {
        #region Constructor

        private PluginHostBuilder()
        {

        }

        #endregion

        #region Private Variables

        private readonly List<PluginRegistration> _registrations = new List<PluginRegistration>();
        private IRecordingManager _manager;
        private Func<string, CancellationToken, Task<byte[]>> _snapshotProvider;
        private Func<string, ICameraPtz> _ptzProvider;
        private Func<string, ICameraImaging> _imagingProvider;
        private Func<string, ICameraIo> _ioProvider;
        private int _frameQueueCapacity = 32;
        private int _notificationQueueCapacity = 1024;
        private int _eventHistoryCapacity = 1000;
        private TimeSpan _stopTimeout = TimeSpan.FromSeconds(10);
        private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private TimeProvider _timeProvider = TimeProvider.System;

        #endregion

        #region Public Methods

        public IPluginHost Build()
        {
            if (_manager == null)
            {
                throw new InvalidOperationException("Plugin Host Builder must have a recording manager.");
            }

            return PluginHost.CreateInstance(_manager, _registrations.ToList(), _snapshotProvider, _frameQueueCapacity, _notificationQueueCapacity, _eventHistoryCapacity, _stopTimeout, _timeProvider, _loggerFactory, _ptzProvider, _imagingProvider, _ioProvider);
        }

        public PluginHostBuilder SetRecordingManager(IRecordingManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            return this;
        }

        /// <summary>
        /// Adds a plugin for <paramref name="cameras"/> (every camera when null) with its <paramref name="settings"/>.
        /// </summary>
        public PluginHostBuilder AddPlugin(IPlugin plugin, IEnumerable<string> cameras = null, IReadOnlyDictionary<string, string> settings = null)
        {
            if (plugin == null)
            {
                throw new ArgumentNullException(nameof(plugin));
            }

            if (string.IsNullOrWhiteSpace(plugin.Name))
            {
                throw new ArgumentException("Plugin name must not be empty.", nameof(plugin));
            }

            if (_registrations.Any(r => string.Equals(r.Plugin.Name, plugin.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"A plugin named '{plugin.Name}' has already been added.", nameof(plugin));
            }

            _registrations.Add(new PluginRegistration(plugin, cameras, settings));
            return this;
        }

        public PluginHostBuilder AddPlugins(IEnumerable<IPlugin> plugins, IEnumerable<string> cameras = null, IReadOnlyDictionary<string, string> settings = null)
        {
            foreach (var plugin in plugins ?? throw new ArgumentNullException(nameof(plugins)))
            {
                AddPlugin(plugin, cameras, settings);
            }

            return this;
        }

        /// <summary>Source of JPEG snapshots for <see cref="IPluginCameras.GetSnapshotAsync"/>.</summary>
        public PluginHostBuilder SetSnapshotProvider(Func<string, CancellationToken, Task<byte[]>> snapshotProvider)
        {
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            return this;
        }

        /// <summary>Source of PTZ control for <see cref="IPluginCameras.GetPtz"/>.</summary>
        public PluginHostBuilder SetPtzProvider(Func<string, ICameraPtz> ptzProvider)
        {
            _ptzProvider = ptzProvider ?? throw new ArgumentNullException(nameof(ptzProvider));
            return this;
        }

        /// <summary>Source of imaging control for <see cref="IPluginCameras.GetImaging"/>.</summary>
        public PluginHostBuilder SetImagingProvider(Func<string, ICameraImaging> imagingProvider)
        {
            _imagingProvider = imagingProvider ?? throw new ArgumentNullException(nameof(imagingProvider));
            return this;
        }

        /// <summary>Source of relay/input control for <see cref="IPluginCameras.GetIo"/>.</summary>
        public PluginHostBuilder SetIoProvider(Func<string, ICameraIo> ioProvider)
        {
            _ioProvider = ioProvider ?? throw new ArgumentNullException(nameof(ioProvider));
            return this;
        }

        /// <summary>Frames queued per plugin before the oldest are dropped. Default: 32.</summary>
        public PluginHostBuilder SetFrameQueueCapacity(int capacity)
        {
            _frameQueueCapacity = RequirePositive(capacity, nameof(capacity));
            return this;
        }

        /// <summary>State changes, segments and events queued per plugin before the oldest are dropped. Default: 1024.</summary>
        public PluginHostBuilder SetNotificationQueueCapacity(int capacity)
        {
            _notificationQueueCapacity = RequirePositive(capacity, nameof(capacity));
            return this;
        }

        /// <summary>Events kept in memory for <see cref="IPluginHost.GetEvents"/>. Default: 1000.</summary>
        public PluginHostBuilder SetEventHistoryCapacity(int capacity)
        {
            _eventHistoryCapacity = RequirePositive(capacity, nameof(capacity));
            return this;
        }

        /// <summary>How long a plugin may take to drain its queues and stop. Default: 10 seconds.</summary>
        public PluginHostBuilder SetStopTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            _stopTimeout = timeout;
            return this;
        }

        public PluginHostBuilder SetLoggerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            return this;
        }

        public PluginHostBuilder SetTimeProvider(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            return this;
        }

        public static PluginHostBuilder GetBuilder()
        {
            return new PluginHostBuilder();
        }

        #endregion

        #region Private Methods

        private static int RequirePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        #endregion
    }
}
