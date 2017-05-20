// Sarthak Killedar
// 2016-03-07 @ 11:37 PM

using System;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Operations;
using Onvif.Open.Device.Management.Implementation.Extention;
using Onvif.Open.Device.Management.Ver10.DeviceManagement;
using DateTime = System.DateTime;

namespace Onvif.Open.Device.Management.Implementation.Client
{
    public class DeviceDateTime : IDeviceDateAndTime, IDeviceDateTimeInformation
    {
        #region Constructor

        private DeviceDateTime(DeviceClient client)
        {
            _client = client;
        }

        #endregion

        #region Private Variables

        private readonly DeviceClient _client;
        #endregion

        #region Public Variables

        public CameraDateTimeType CameraDateTimeType { get; private set; }
        public bool DaylightSavings { get; private set; }
        public string TimeZone { get; private set; }
        public DateTime UtctDateTime { get; private set; }
        public DateTime? LocalDateTime { get; private set; }

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        public IDeviceDateAndTime Get()
        {
            Parse(_client.GetSystemDateAndTime());
            return this;
        }

        private void Parse(SystemDateTime deviceDateTime)
        {
            CameraDateTimeType = deviceDateTime.DateTimeType == SetDateTimeType.Manual
                ? CameraDateTimeType.Manual
                : CameraDateTimeType.NetworkTimeProvider;
            DaylightSavings = deviceDateTime.DaylightSavings;
            TimeZone = deviceDateTime.TimeZone.TZ;
            UtctDateTime = new DateTime().CreateDateTimeFromOnvifDateTime(deviceDateTime.UTCDateTime);
            LocalDateTime = new DateTime().CreateDateTimeFromOnvifDateTime(deviceDateTime.LocalDateTime);
        }

        internal static IDeviceDateTimeInformation CreateInstance(DeviceClient client)
        {
            return new DeviceDateTime(client);
        }

        #endregion

    }
}