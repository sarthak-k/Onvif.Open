// Sarthak Killedar
// 2016-03-06 @ 12:47 AM

using System;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Core.Abstract.Interface.Device
{
    public interface IDeviceDateAndTime
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        CameraDateTimeType CameraDateTimeType { get; }
        bool DaylightSavings { get; }
        string TimeZone { get; }
        DateTime UtctDateTime { get; }
        DateTime? LocalDateTime { get; }
        
        //TODO : Introduce Extension (SystemDateTimeExtension)
        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        #endregion

    }
}