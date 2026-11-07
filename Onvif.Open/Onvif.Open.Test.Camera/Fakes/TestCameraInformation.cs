// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Test.Camera.Fakes
{
    /// <summary>
    /// Camera information pointing at a loopback fake device (CameraBuilder rejects loopback URLs by design).
    /// </summary>
    internal sealed class TestCameraInformation : ICameraInformation
    {
        public TestCameraInformation(Uri cameraUri, string username = "admin", string password = "12345")
        {
            CameraUri = cameraUri;
            Username = username;
            Password = password;
        }

        public Uri CameraUri { get; }
        public string Username { get; }
        public string Password { get; }
    }
}
