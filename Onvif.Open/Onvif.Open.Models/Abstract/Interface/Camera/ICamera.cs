using System;
using Onvif.Open.Core.Abstract.Interface.Device;

namespace Onvif.Open.Core.Abstract.Interface.Camera
{
    public interface ICamera
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        ICameraDeviceManagement Initialize();
        ICameraInformation GetCameraInformation();
        ICameraMedia InitializeMedia();

        /// <summary>The device's events (motion, tampering, inputs, ...).</summary>
        Events.ICameraEvents InitializeEvents();

        /// <summary>Pan/tilt/zoom control (cameras with PTZ only).</summary>
        Ptz.ICameraPtz InitializePtz();

        /// <summary>Image settings (brightness, day/night, ...) and focus (cameras with an Imaging service).</summary>
        Imaging.ICameraImaging InitializeImaging();

        /// <summary>
        /// Measures the device clock and stamps all further WS-Security tokens in device time.
        /// Media operations do this automatically (and again when the device rejects a token);
        /// call it before using device management on a camera whose clock may be off.
        /// Returns device time minus local time.
        /// </summary>
        System.TimeSpan SynchronizeClock();

        #endregion
    }
}