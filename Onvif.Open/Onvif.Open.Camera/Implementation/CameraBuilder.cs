// Sarthak Killedar
// 2016-03-03 @ 10:11 PM

using System;
using Onvif.Open.Camera.Interface;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Camera.Implementation
{
    public sealed class CameraBuilder : ICameraBuilder
    {
        #region Constructor

        private CameraBuilder()
        {
            
        }

        #endregion

        #region Private Variables

        private Uri _cameraUri;
        private Uri CameraUrl { get; set; }
        private string Username { get; set; }
        private string Password { get; set; }
        private static string OnvifDeviceServiceUri => "/onvif/device_service";
        
        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        private bool ParseUri(string url)
        {
            var isValid = Uri.TryCreate(url, UriKind.Absolute, out _cameraUri);
            return isValid;
        }

        private bool SetHostname()
        {
            var isSucess = !IsNull() && IsAbsolute() && !IsLoopback() && !IsUnknown();

            if (isSucess)
            {
                SetOnvif();
                return true;
            }
            else
            {
                return false;
            }

        }

        private bool IsNull()
        {
            return _cameraUri == null;
        }

        private void SetOnvif()
        {
            CameraUrl = new Uri(string.Concat(_cameraUri.Scheme, Uri.SchemeDelimiter, _cameraUri.Authority, OnvifDeviceServiceUri));
        }


        private bool IsUnknown()
        {
            return _cameraUri.HostNameType == UriHostNameType.Unknown;
        }

        private bool IsLoopback()
        {
            return _cameraUri.IsLoopback;
        }

        private bool IsAbsolute()
        {
            return _cameraUri.IsAbsoluteUri;
        }

        #endregion

        #region Public Methods

        public ICamera Build()
        {
            return Camera.CreateInstance(CameraUrl, Username, Password);
        }

        public CameraBuilder SetUrl(string url)
        {
            var isValid = ParseUri(url);

            if (isValid)
            {
                var isSuccess = SetHostname();

                if (!isSuccess)
                {
                    throw new UriFormatException("Provided Camera URL is incorrect or malfunctioned.");
                }
            }
            else
            {
                throw new UriFormatException("Provided Camera URL is incorrect or malfunctioned.");
            }

            return this;
        }

        public CameraBuilder SetUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentNullException(nameof(username));
            }

            Username = username;

            return this;
        }

        public CameraBuilder SetPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(password));
            }

            Password = password;

            return this;
        }

        public static CameraBuilder GetBuilder()
        {
            return new CameraBuilder();
        }

        #endregion

    }
}