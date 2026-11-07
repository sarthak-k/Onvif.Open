// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.DeviceIo;

namespace Onvif.Open.Core.Abstract.Interface.DeviceIo
{
    /// <summary>
    /// ONVIF DeviceIO service (ver10): digital inputs.
    /// </summary>
    public interface IDeviceIoService
    {
        #region Public Variables

        Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        Task<IReadOnlyList<DigitalInput>> GetDigitalInputsAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
