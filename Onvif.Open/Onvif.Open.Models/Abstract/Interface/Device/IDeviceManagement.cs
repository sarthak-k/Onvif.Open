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

        #endregion

    }
}