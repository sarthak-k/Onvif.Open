// Sarthak Killedar
// 2016-03-06 @ 1:08 AM
namespace Onvif.Open.Core.Abstract.Interface.Device
{
    public interface IDeviceManagement
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

        IDeviceInfo GetDeviceInfo();
        IDeviceDateAndTime GetDeviceDateTime();

        /// <summary>
        /// Address of the device's Media service (from GetCapabilities). Falls back to the device service address.
        /// </summary>
        System.Uri GetMediaServiceUri();

        /// <summary>
        /// Address of the device's Events service (from GetCapabilities). Falls back to the device service address.
        /// </summary>
        System.Uri GetEventServiceUri();

        /// <summary>
        /// Address of the device's PTZ service (from GetCapabilities); null when the device reports no PTZ.
        /// Falls back to the device service address when the device does not implement GetCapabilities.
        /// </summary>
        System.Uri GetPtzServiceUri();

        /// <summary>Address of the device's Imaging service (from GetCapabilities); null when the device reports none.</summary>
        System.Uri GetImagingServiceUri();

        /// <summary>
        /// Address of the service with <paramref name="serviceNamespace"/> (see OnvifServiceNamespaces) from GetServices;
        /// null when the device does not offer it or does not implement GetServices.
        /// </summary>
        System.Uri FindServiceUri(string serviceNamespace);

        /// <summary>
        /// Device clock minus local clock, measured with an unauthenticated GetSystemDateAndTime
        /// (allowed by the ONVIF Core Specification precisely so clients can synchronize).
        /// Null when the device does not report its UTC time.
        /// </summary>
        System.TimeSpan? GetClockOffset();

        #endregion

    }
}