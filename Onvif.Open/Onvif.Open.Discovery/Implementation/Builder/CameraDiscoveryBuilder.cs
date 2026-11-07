// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Net;
using Onvif.Open.Core.Abstract.Interface.Discovery;
using Onvif.Open.Discovery.Interface;

namespace Onvif.Open.Discovery.Implementation.Builder
{
    /// <summary>
    /// Builds a WS-Discovery client.
    /// <code>
    /// var devices = await CameraDiscoveryBuilder.GetBuilder().Build().FindAsync();
    /// </code>
    /// </summary>
    public sealed class CameraDiscoveryBuilder : ICameraDiscoveryBuilder
    {
        #region Constructor

        private CameraDiscoveryBuilder()
        {

        }

        #endregion

        #region Private Variables

        private readonly List<IPEndPoint> _targets = new List<IPEndPoint>();
        private TimeSpan _timeout = TimeSpan.FromSeconds(3);
        private bool _multicast = true;

        #endregion

        #region Public Methods

        public ICameraDiscovery Build()
        {
            if (!_multicast && _targets.Count == 0)
            {
                throw new InvalidOperationException("Discovery needs multicast or at least one unicast target.");
            }

            return CameraDiscovery.CreateInstance(_timeout, _multicast, _targets.ToArray());
        }

        /// <summary>How long to wait for answers. Default: 3 seconds (devices answer within 0-500 ms plus network latency).</summary>
        public CameraDiscoveryBuilder SetTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            _timeout = timeout;
            return this;
        }

        /// <summary>Probe the local network's multicast group (default: on).</summary>
        public CameraDiscoveryBuilder SetMulticast(bool enabled)
        {
            _multicast = enabled;
            return this;
        }

        /// <summary>
        /// Also probe <paramref name="address"/> directly, e.g. a camera on another subnet that multicast does not reach.
        /// </summary>
        public CameraDiscoveryBuilder AddTarget(IPAddress address, int port = 3702)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            _targets.Add(new IPEndPoint(address, port));
            return this;
        }

        public static CameraDiscoveryBuilder GetBuilder()
        {
            return new CameraDiscoveryBuilder();
        }

        #endregion
    }
}
