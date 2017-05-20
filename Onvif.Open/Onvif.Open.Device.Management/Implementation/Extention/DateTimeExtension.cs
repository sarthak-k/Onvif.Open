using System;

namespace Onvif.Open.Device.Management.Implementation.Extention
{
    public static class DateTimeExtension
    {
        #region Constructor

        #endregion

        #region Public Variables

        #endregion

        #region Private Variables

        #endregion

        #region Public Methods

        public static DateTime CreateDateTimeFromOnvifDateTime(this DateTime dateTime, Ver10.DeviceManagement.DateTime providedDateTime)
        {
            return new DateTime(providedDateTime.Date.Year, providedDateTime.Date.Month, providedDateTime.Date.Day, providedDateTime.Time.Hour, providedDateTime.Time.Minute, providedDateTime.Time.Second);
        }
        #endregion

        #region Private Methods

        #endregion

        #region Static Methods

        #endregion
    }
}