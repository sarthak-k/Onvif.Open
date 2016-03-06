// Sarthak Killedar
// 2016-03-06 @ 12:43 AM
namespace Onvif.Open.Core.Abstract.Interface.Device
{
    public interface IDeviceInfo
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        string Manufacturer { get; }
        string Model { get; }
        string FirmwareVersion { get;}
        string SerialNumber { get; }
        string HardwareId { get; }

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        #endregion


    }
}