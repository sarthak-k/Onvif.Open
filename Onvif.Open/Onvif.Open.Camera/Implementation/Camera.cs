// Sarthak Killedar
// 2016-03-03 @ 9:57 PM

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Camera.Implementation
{
    public class Camera : ICamera, ICameraInformation
    {

        #region Constructor

        private Camera(Uri cameraUri, string username, string password)
        {
            CameraUri = cameraUri;
            Username = username;
            Password = password;
        }

        #endregion

        #region Private Variables

        private ICameraDeviceManagement CameraDeviceManagement { get; set; }

        private ICameraMedia CameraMedia { get; set; }

        private Core.Abstract.Interface.Events.ICameraEvents CameraEvents { get; set; }

        private ClockSynchronizer _clock;
        private readonly object _clockSync = new object();
        
        #endregion

        #region Public Variables


        public Uri CameraUri { get; }
        public string Username { get; }
        public string Password { get; }

        public ICameraDeviceManagement Initialize()
        {
            return CameraDeviceManagement ??
                   (CameraDeviceManagement =
                       CameraDeviceManagementBuilder.GetBuilder().SetDeviceManagement(this).Build());
        }

        public ICameraMedia InitializeMedia()
        {
            return CameraMedia ??
                   (CameraMedia =
                       CameraMediaBuilder.GetBuilder().SetCamera(this).SetDeviceManagement(Initialize()).SetClockSynchronizer(GetClock()).Build());
        }

        public Core.Abstract.Interface.Events.ICameraEvents InitializeEvents()
        {
            return CameraEvents ??
                   (CameraEvents = Implementation.CameraEvents.CreateInstance(this, Initialize().GetDeviceManagement(), GetClock()));
        }

        public TimeSpan SynchronizeClock()
        {
            return GetClock().Synchronize();
        }

        /// <summary>Device time minus local time, as last measured (zero until synchronized).</summary>
        public TimeSpan ClockOffset => GetClock().Offset;

        public ICameraInformation GetCameraInformation()
        {
            return this;
        }

        #endregion

        #region Private Methods

        private ClockSynchronizer GetClock()
        {
            lock (_clockSync)
            {
                return _clock ?? (_clock = new ClockSynchronizer(this, Initialize().GetDeviceManagement()));
            }
        }

        internal static ICamera CreateInstance(Uri cameraUri, string username, string password)
        {
            return new Camera(cameraUri, username, password);
        }


        #endregion

        #region Public Methods

        #endregion

    }
}