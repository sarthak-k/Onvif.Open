// Sarthak Killedar
// 2016-03-06 @ 10:51 AM

using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Device.Management.Ver10.DeviceManagement;

namespace Onvif.Open.Device.Management.Implementation.Client
{
    public class DeviceInformation : IDeviceInfo, IDeviceInformation
    {
        #region Constructor

        private DeviceInformation(DeviceClient client)
        {
            _client = client;
        }

        #endregion

        #region Private Variables

        private readonly DeviceClient _client;

        #endregion

        #region Public Variables
        public string Manufacturer => _manufacturer;
        public string Model => _model;
        public string FirmwareVersion => _firmwareVersion;
        public string SerialNumber => _serialNumber;
        public string HardwareId => _hardwareId;

        private string _manufacturer;
        private string _model;
        private string _firmwareVersion;
        private string _serialNumber;
        private string _hardwareId;

        #endregion

        #region Private Methods


        #endregion

        #region Public Methods

        internal static IDeviceInformation CreateInstance(DeviceClient client)
        {
            return new DeviceInformation(client);
        }

        public IDeviceInfo Get()
        {
            _manufacturer = _client.GetDeviceInformation(out _model, out _firmwareVersion, out _serialNumber,
                out _hardwareId);
            return this;
        }

        #endregion

    }
}