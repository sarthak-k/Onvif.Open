// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.DeviceIo;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: relay outputs and digital inputs of ONVIF cameras.
    /// </summary>
    internal static class DeviceIoEndpoints
    {
        #region Public Variables

        public sealed class RelayRequest
        {
            public bool Active { get; set; } = true;

            /// <summary>Activate for this many milliseconds, then deactivate (max 600000).</summary>
            public int? DurationMs { get; set; }
        }

        public sealed class RelaySettingsRequest
        {
            public RelayMode Mode { get; set; } = RelayMode.Monostable;
            public int DelayMs { get; set; }
            public IdleState IdleState { get; set; } = IdleState.Open;
        }

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapDeviceIoApi(this IEndpointRouteBuilder app)
        {
            var io = app.MapGroup("/api/cameras/{id}/io");

            io.MapGet("", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i =>
                {
                    var relays = await i.GetRelayOutputsAsync(ct);
                    IReadOnlyList<DigitalInput> inputs;

                    try
                    {
                        inputs = await i.GetDigitalInputsAsync(ct);
                    }
                    catch (NotSupportedException)
                    {
                        // Relays work without the DeviceIO service; inputs are then unknown.
                        inputs = null;
                    }

                    return Results.Ok(new { relays, inputs });
                }));

            io.MapPost("/relays/{token}", (string id, string token, RelayRequest request, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i =>
                {
                    if (request?.DurationMs is int ms)
                    {
                        await i.PulseRelayAsync(token, TimeSpan.FromMilliseconds(Math.Clamp(ms, 1, 600000)), ct);
                    }
                    else
                    {
                        await i.SetRelayAsync(token, request?.Active ?? true, ct);
                    }

                    return Results.NoContent();
                }));

            io.MapPut("/relays/{token}/settings", (string id, string token, RelaySettingsRequest request, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i =>
                {
                    request = request ?? new RelaySettingsRequest();
                    await i.ConfigureRelayAsync(token, request.Mode, TimeSpan.FromMilliseconds(Math.Max(0, request.DelayMs)), request.IdleState, ct);
                    return Results.NoContent();
                }));

            return app;
        }

        #endregion

        #region Private Methods

        private static async Task<IResult> Run(string id, IRecordingManager manager, ConfiguredRecording recording, Func<ICameraIo, Task<IResult>> action)
        {
            var cameraId = manager.GetStatus().FirstOrDefault(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase))?.CameraId;
            if (cameraId == null)
            {
                return Results.NotFound();
            }

            try
            {
                return await action(recording.GetIo(cameraId));
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
            catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        #endregion
    }
}
