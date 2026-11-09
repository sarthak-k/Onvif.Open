// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Operations;
using Onvif.Open.Streaming.Implementation.Builder;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// Tests a camera before (or after) it is saved: can the recorder reach and sign in to it, and what does it offer
    /// (device, media profiles, PTZ, imaging, analytics, replay, I/O; codec and audio of RTSP streams).
    /// </summary>
    public static class CameraProbe
    {
        #region Private Variables

        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        #endregion

        #region Public Methods

        /// <summary>A snapshot is fetched within this time, or left out.</summary>
        public static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(5);

        /// <param name="snapshot">Also fetch a picture (ONVIF cameras, from the sub-stream when there is one).</param>
        public static async Task<ProbeResult> ProbeAsync(CameraOptions camera, CancellationToken cancellationToken, bool snapshot = false)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(Timeout);

                try
                {
                    return string.IsNullOrWhiteSpace(camera.OnvifUrl)
                        ? await ProbeRtspAsync(camera, timeout.Token).ConfigureAwait(false)
                        : await ProbeOnvifAsync(camera, snapshot, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ProbeResult.Failed("unreachable", $"The camera did not answer within {Timeout.TotalSeconds:0} seconds.");
                }
                catch (UnauthorizedAccessException ex)
                {
                    return ProbeResult.Failed("unauthorized", ex.Message);
                }
                catch (Exception ex) when (Core.Implementation.OnvifFaults.IsAuthenticationFailure(ex))
                {
                    return ProbeResult.Failed("unauthorized", "The camera refused the user name or password.");
                }
                catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException || ex is System.IO.IOException || ex is System.Net.Sockets.SocketException || ex is InvalidOperationException || ex is NotSupportedException || ex is UriFormatException || ex is ArgumentException)
                {
                    return ProbeResult.Failed("unreachable", ex.Message);
                }
            }
        }

        #endregion

        #region Private Methods

        private static async Task<ProbeResult> ProbeOnvifAsync(CameraOptions options, bool snapshot, CancellationToken cancellationToken)
        {
            var builder = CameraBuilder.GetBuilder().SetUrl(options.OnvifUrl).SetMediaVersion(options.MediaVersion);

            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                builder.SetUsername(options.Username);
            }

            if (!string.IsNullOrEmpty(options.Password))
            {
                builder.SetPassword(options.Password);
            }

            var camera = builder.Build();

            // The client APIs used here are synchronous: keep them off the request thread, and cancellable.
            return await Task.Run(async () =>
            {
                camera.SynchronizeClock();
                var device = camera.Initialize().GetDeviceManagement();
                var info = device.GetDeviceInfo();
                cancellationToken.ThrowIfCancellationRequested();

                var media = camera.InitializeMedia();
                var profiles = await media.GetMediaService().GetProfilesAsync(cancellationToken).ConfigureAwait(false);

                var result = new ProbeResult
                {
                    Ok = true,
                    Device = new ProbeDevice
                    {
                        Manufacturer = info?.Manufacturer,
                        Model = info?.Model,
                        FirmwareVersion = info?.FirmwareVersion,
                        SerialNumber = info?.SerialNumber
                    },
                    Profiles = profiles.Select(p => new ProbeProfile
                    {
                        Token = p.Token,
                        Name = p.Name,
                        Encoding = p.VideoEncoding,
                        Width = p.Width,
                        Height = p.Height,
                        FrameRate = p.FrameRateLimit,
                        Bitrate = p.BitrateLimit,
                        Ptz = p.PtzConfigurationToken != null,
                        Analytics = p.MetadataConfigurationToken != null
                    }).ToList(),
                    Capabilities = new ProbeCapabilities
                    {
                        Ptz = device.GetPtzServiceUri() != null,
                        Imaging = device.GetImagingServiceUri() != null,
                        Analytics = profiles.Any(p => p.MetadataConfigurationToken != null),
                        Replay = device.FindServiceUri(OnvifServiceNamespaces.Replay) != null,
                        DeviceIo = device.FindServiceUri(OnvifServiceNamespaces.DeviceIo) != null
                    }
                };

                result.Recommended = StreamSelection.Recommend(result.Profiles);
                if (snapshot)
                {
                    result.Snapshot = await SnapshotAsync(media, result.Recommended.SubProfile ?? result.Recommended.MainProfile, SnapshotTimeout, cancellationToken).ConfigureAwait(false);
                }

                return result;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The picture as a data URL, or null when the camera has none to give in time.</summary>
        internal static async Task<string> SnapshotAsync(Core.Abstract.Interface.Camera.ICameraMedia media, string profileToken, TimeSpan within, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(within);
                try
                {
                    var jpeg = await media.GetSnapshotAsync(profileToken, timeout.Token).ConfigureAwait(false);
                    return jpeg == null || jpeg.Length == 0 ? null : "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !(ex is OutOfMemoryException))
                {
                    // A camera without snapshots still works; the picture is a convenience.
                    return null;
                }
            }
        }

        private static async Task<ProbeResult> ProbeRtspAsync(CameraOptions options, CancellationToken cancellationToken)
        {
            var builder = LiveVideoStreamBuilder.GetBuilder()
                .SetStreamUri(new Uri(options.RtspUrl))
                .SetTransport(options.Transport)
                .SetAudio(options.Transport == StreamTransport.Rtsp)
                .SetTimeout(Timeout);

            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                builder.SetCredentials(options.Username, options.Password);
            }

            await using (var stream = await builder.BuildAsync(cancellationToken).ConfigureAwait(false))
            {
                return new ProbeResult
                {
                    Ok = true,
                    Stream = new ProbeStream { Codec = stream.Codec.ToString(), Audio = stream.Audio?.ToString() }
                };
            }
        }

        #endregion
    }

    /// <summary>
    /// What <see cref="CameraProbe"/> found; <see cref="Error"/> and <see cref="Kind"/> ("unauthorized", "unreachable") on failure.
    /// </summary>
    public sealed class ProbeResult
    {
        public bool Ok { get; set; }
        public string Kind { get; set; }
        public string Error { get; set; }
        public ProbeDevice Device { get; set; }
        public List<ProbeProfile> Profiles { get; set; }
        public ProbeCapabilities Capabilities { get; set; }
        public ProbeStream Stream { get; set; }

        /// <summary>The streams to use (ONVIF cameras), chosen by <see cref="StreamSelection"/>.</summary>
        public StreamRecommendation Recommended { get; set; }

        /// <summary>A picture from the camera as a data URL (when asked for and available).</summary>
        public string Snapshot { get; set; }

        public static ProbeResult Failed(string kind, string error) => new ProbeResult { Ok = false, Kind = kind, Error = error };
    }

    public sealed class ProbeDevice
    {
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string FirmwareVersion { get; set; }
        public string SerialNumber { get; set; }
    }

    public sealed class ProbeProfile
    {
        public string Token { get; set; }
        public string Name { get; set; }
        public string Encoding { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public int? FrameRate { get; set; }
        public int? Bitrate { get; set; }
        public bool Ptz { get; set; }
        public bool Analytics { get; set; }
    }

    public sealed class ProbeCapabilities
    {
        public bool Ptz { get; set; }
        public bool Imaging { get; set; }
        public bool Analytics { get; set; }
        public bool Replay { get; set; }
        public bool DeviceIo { get; set; }
    }

    public sealed class ProbeStream
    {
        public string Codec { get; set; }
        public string Audio { get; set; }
    }
}
