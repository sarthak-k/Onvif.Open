// Sarthak Killedar
// 2016-03-06 @ 12:47 AM

using System;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Core.Abstract.Interface.Device
{
    public interface ISystemDateAndTime
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        CameraDateTimeType CameraDateTimeType { get; set; }
        bool DaylightSavings { get; set; }
        string TimeZone { get; set; }
        DateTime UtctDateTime { get; set; }
        DateTime? LocalDateTime { get; set; }
        
        //TODO : Introduce Extension (SystemDateTimeExtension)
        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        #endregion

    }
}