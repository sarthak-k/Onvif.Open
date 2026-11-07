// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Plugins.Implementation.Runtime
{
    /// <summary>
    /// Camera access restricted to the cameras assigned to one plugin (least privilege).
    /// </summary>
    internal sealed class PluginCameras : IPluginCameras
    {
        #region Constructor

        public PluginCameras(IRecordingManager manager, IReadOnlyList<string> assigned, Func<string, CancellationToken, Task<byte[]>> snapshotProvider, Func<string, ICameraPtz> ptzProvider = null, Func<string, ICameraImaging> imagingProvider = null, Func<string, ICameraIo> ioProvider = null)
        {
            _ioProvider = ioProvider;
            _ptzProvider = ptzProvider;
            _imagingProvider = imagingProvider;
            _manager = manager;
            _assigned = assigned == null ? null : new HashSet<string>(assigned, StringComparer.OrdinalIgnoreCase);
            _snapshotProvider = snapshotProvider;
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;
        private readonly HashSet<string> _assigned;
        private readonly Func<string, CancellationToken, Task<byte[]>> _snapshotProvider;
        private readonly Func<string, ICameraPtz> _ptzProvider;
        private readonly Func<string, ICameraImaging> _imagingProvider;
        private readonly Func<string, ICameraIo> _ioProvider;

        #endregion

        #region Public Methods

        public bool IsAssigned(string cameraId)
        {
            return cameraId != null && (_assigned == null || _assigned.Contains(cameraId));
        }

        public IReadOnlyList<CameraRecordingStatus> GetStatus()
        {
            return _manager.GetStatus().Where(s => IsAssigned(s.CameraId)).ToList();
        }

        public IReadOnlyList<RecordingSegment> GetSegments(string cameraId)
        {
            return _manager.GetSegments(Require(cameraId));
        }

        public Task StartRecordingAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            return _manager.StartCameraAsync(Require(cameraId), cancellationToken);
        }

        public Task StopRecordingAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            return _manager.StopCameraAsync(Require(cameraId), cancellationToken);
        }

        public ICameraPtz GetPtz(string cameraId)
        {
            Require(cameraId);

            if (_ptzProvider == null)
            {
                throw new NotSupportedException("PTZ control is not available in this host.");
            }

            return _ptzProvider(cameraId);
        }

        public ICameraImaging GetImaging(string cameraId)
        {
            Require(cameraId);

            if (_imagingProvider == null)
            {
                throw new NotSupportedException("Imaging control is not available in this host.");
            }

            return _imagingProvider(cameraId);
        }

        public ICameraIo GetIo(string cameraId)
        {
            Require(cameraId);

            if (_ioProvider == null)
            {
                throw new NotSupportedException("Device IO control is not available in this host.");
            }

            return _ioProvider(cameraId);
        }

        public void TriggerRecording(string cameraId, TimeSpan duration)
        {
            _manager.TriggerRecording(Require(cameraId), duration);
        }

        public Task<byte[]> GetSnapshotAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            Require(cameraId);

            if (_snapshotProvider == null)
            {
                throw new NotSupportedException("Snapshots are not available in this host.");
            }

            return _snapshotProvider(cameraId, cancellationToken);
        }

        #endregion

        #region Private Methods

        private string Require(string cameraId)
        {
            if (!IsAssigned(cameraId))
            {
                throw new UnauthorizedAccessException($"Camera '{cameraId}' is not assigned to this plugin.");
            }

            return cameraId;
        }

        #endregion
    }
}
