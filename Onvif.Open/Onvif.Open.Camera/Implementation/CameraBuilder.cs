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
        private Core.Operations.MediaServiceVersion MediaVersion { get; set; }
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

        private void SetOnvif()
        {
            CameraUrl = new Uri(string.Concat(_cameraUri.Scheme, Uri.SchemeDelimiter, _cameraUri.Authority, OnvifDeviceServiceUri));
        }


        private void IsUnknown()
        {
            if (_cameraUri.HostNameType == UriHostNameType.Unknown)
            {
                throw new UriFormatException("Camera URL provided is of Unknown HostName Type.");
            }
        }

        private void IsLoopback()
        {
            if (_cameraUri.IsLoopback)
            {
                throw new UriFormatException("Camera URL provided is Loopback URL.");
            }
        }

        private void IsAbsolute()
        {
            if (!_cameraUri.IsAbsoluteUri)
            {
                throw new UriFormatException("Camera URL provided is not Absolute URL.");
            }
        }

        #endregion

        #region Public Methods

        public ICamera Build()
        {
            if (_cameraUri == null)
            {
                throw new InvalidOperationException("Camera Builder must have Camera URL to Build Camera.");
            }

            return Camera.CreateInstance(CameraUrl, Username, Password, MediaVersion);
        }

        public CameraBuilder SetUrl(string url)
        {
            var isValid = ParseUri(url);

            if (isValid)
            {
                IsAbsolute();

                IsLoopback();

                IsUnknown();

                SetOnvif();
            }
            else
            {
                throw new UriFormatException("Provided Camera URL is incorrect or malfunctioned.");
            }

            return this;
        }

        /// <summary>
        /// Uses <paramref name="url"/> as the exact device service address (keeping its path), e.g. the
        /// <c>DeviceServiceUri</c> of a device found with WS-Discovery.
        /// </summary>
        public CameraBuilder SetDeviceServiceUrl(string url)
        {
            SetUrl(url);

            if (!string.IsNullOrEmpty(_cameraUri.AbsolutePath) && _cameraUri.AbsolutePath != "/")
            {
                CameraUrl = new Uri(_cameraUri.GetLeftPart(UriPartial.Path));
            }

            return this;
        }

        /// <summary>
        /// Which media service to use. Default <see cref="Core.Operations.MediaServiceVersion.Auto"/>: Media2 (H.265 aware)
        /// when the camera offers it, otherwise Media1. Force Media1 for devices with a faulty Media2 implementation.
        /// </summary>
        public CameraBuilder SetMediaVersion(Core.Operations.MediaServiceVersion version)
        {
            MediaVersion = version;
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