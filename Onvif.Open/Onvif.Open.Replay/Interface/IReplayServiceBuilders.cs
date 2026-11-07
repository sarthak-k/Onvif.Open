// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Replay.Implementation.Builder;

namespace Onvif.Open.Replay.Interface
{
    internal interface IRecordingSearchServiceBuilder
    {
        #region Public Methods

        IRecordingSearchService Build();
        RecordingSearchServiceBuilder SetCamera(ICameraInformation camera);
        RecordingSearchServiceBuilder SetServiceUri(Uri serviceUri);

        #endregion
    }

    internal interface IReplayServiceBuilder
    {
        #region Public Methods

        IReplayService Build();
        ReplayServiceBuilder SetCamera(ICameraInformation camera);
        ReplayServiceBuilder SetServiceUri(Uri serviceUri);

        #endregion
    }
}
