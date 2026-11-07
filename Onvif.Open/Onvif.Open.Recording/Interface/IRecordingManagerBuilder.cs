// Sarthak Killedar
// 2026-11-07

using System;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recording.Implementation.Builder;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Recording.Implementation.Storage;

namespace Onvif.Open.Recording.Interface
{
    internal interface IRecordingManagerBuilder
    {
        #region Public Methods

        IRecordingManager Build();
        RecordingManagerBuilder SetStorageDirectory(string directory);
        RecordingManagerBuilder SetStorage(IRecordingStorage storage);
        RecordingManagerBuilder SetArchive(Core.Abstract.Interface.Storage.ISegmentStore store, Implementation.Archive.SegmentArchiveOptions options = null);
        RecordingManagerBuilder SetFormat(ISegmentFormat format);
        RecordingManagerBuilder SetRotationPolicy(IRotationPolicy rotationPolicy);
        RecordingManagerBuilder SetRetentionPolicy(IRetentionPolicy retentionPolicy);
        RecordingManagerBuilder SetRetentionInterval(TimeSpan interval);
        RecordingManagerBuilder SetReconnectDelay(TimeSpan minimum, TimeSpan maximum);
        RecordingManagerBuilder SetStallTimeout(TimeSpan timeout);
        RecordingManagerBuilder SetLoggerFactory(ILoggerFactory loggerFactory);
        RecordingManagerBuilder SetTimeProvider(TimeProvider timeProvider);

        #endregion
    }
}
