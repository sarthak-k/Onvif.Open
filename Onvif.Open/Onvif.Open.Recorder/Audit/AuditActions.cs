// Sarthak Killedar
// 2026-11-09

namespace Onvif.Open.Recorder.Audit
{
    /// <summary>
    /// What the audit log records: "area.verb". Settings changes, sign-ins, and who watched or took video.
    /// </summary>
    public static class AuditActions
    {
        public const string Setup = "organization.setup";
        public const string OrganizationRename = "organization.rename";

        public const string SignIn = "auth.signin";
        public const string SignInFailed = "auth.signin_failed";
        public const string PasswordChange = "auth.password_change";

        public const string UserCreate = "user.create";
        public const string UserUpdate = "user.update";
        public const string UserDelete = "user.delete";

        public const string SiteCreate = "site.create";
        public const string SiteUpdate = "site.update";
        public const string SiteDelete = "site.delete";

        public const string PolicyCreate = "policy.create";
        public const string PolicyUpdate = "policy.update";
        public const string PolicyDelete = "policy.delete";

        public const string CameraCreate = "camera.create";
        public const string CameraUpdate = "camera.update";
        public const string CameraDelete = "camera.delete";

        public const string PluginUpdate = "plugin.update";
        public const string GatewayUpdate = "gateway.update";
        public const string PluginDelete = "plugin.delete";

        public const string LayoutCreate = "layout.create";
        public const string LayoutUpdate = "layout.update";
        public const string LayoutDelete = "layout.delete";

        /// <summary>Opened live video (recorded once per user and camera every 10 minutes).</summary>
        public const string VideoLive = "video.live";

        /// <summary>Played recordings (once per user and camera every 10 minutes).</summary>
        public const string VideoPlayback = "video.playback";

        /// <summary>Downloaded a clip, a recorded file or a recording from the camera's own storage.</summary>
        public const string VideoExport = "video.export";

        public const string RecordingDelete = "recording.delete";
        public const string RecordingStart = "recording.start";
        public const string RecordingStop = "recording.stop";
        public const string RecordingTrigger = "recording.trigger";
        public const string RelaySwitch = "io.relay";
    }
}
