// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;
using Onvif.Open.Core.Abstract.Interface.Discovery;
using Onvif.Open.Discovery.Implementation.Builder;

namespace Onvif.Open.Discovery.Interface
{
    internal interface ICameraDiscoveryBuilder
    {
        #region Public Methods

        ICameraDiscovery Build();
        CameraDiscoveryBuilder SetTimeout(TimeSpan timeout);
        CameraDiscoveryBuilder SetMulticast(bool enabled);
        CameraDiscoveryBuilder AddTarget(IPAddress address, int port = 3702);

        #endregion
    }
}
