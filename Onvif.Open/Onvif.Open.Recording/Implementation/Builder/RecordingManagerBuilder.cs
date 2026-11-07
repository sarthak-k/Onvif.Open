// Sarthak Killedar
// 2026-11-07

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Recording.Implementation.Policy;
using Onvif.Open.Recording.Implementation.Recorder;
using Onvif.Open.Recording.Implementation.Storage;
using Onvif.Open.Recording.Interface;

namespace Onvif.Open.Recording.Implementation.Builder
{
    /// <summary>
    /// Builds a <see cref="RecordingManager"/>.
    /// <code>
    /// await using var recorder = RecordingManagerBuilder.GetBuilder()
    ///     .SetStorageDirectory("/var/recordings")
    ///     .SetRotationPolicy(RotationPolicies.Every(TimeSpan.FromMinutes(10)))
    ///     .SetRetentionPolicy(RetentionPolicies.KeepFor(TimeSpan.FromDays(7)))
    ///     .Build();
    ///
    /// recorder.AddCamera("front-door", camera);
    /// await recorder.StartAsync();
    /// </code>
    /// </summary>
    public sealed class RecordingManagerBuilder : IRecordingManagerBuilder
    {
        #region Constructor

        private RecordingManagerBuilder()
        {

        }

        #endregion

        #region Private Variables

        private IRecordingStorage _storage;
        private ISegmentFormat _format = SegmentFormats.MpegTs;
        private IRotationPolicy _rotationPolicy = RotationPolicies.Default;
        private IRetentionPolicy _retentionPolicy = RetentionPolicies.Default;
        private TimeSpan _retentionInterval = TimeSpan.FromMinutes(1);
        private readonly RecorderOptions _recorderOptions = new RecorderOptions();
        private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private TimeProvider _timeProvider = TimeProvider.System;

        #endregion

        #region Public Methods

        public IRecordingManager Build()
        {
            if (_storage == null)
            {
                throw new InvalidOperationException("Recording Manager Builder must have a storage (SetStorageDirectory or SetStorage).");
            }

            return RecordingManager.CreateInstance(_storage, _format, _rotationPolicy, _retentionPolicy, _retentionInterval, _recorderOptions, _timeProvider, _loggerFactory);
        }

        public RecordingManagerBuilder SetStorageDirectory(string directory)
        {
            _storage = new FileSystemRecordingStorage(directory);
            return this;
        }

        public RecordingManagerBuilder SetStorage(IRecordingStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            return this;
        }

        /// <summary>Segment file format. Default: <see cref="SegmentFormats.MpegTs"/>.</summary>
        public RecordingManagerBuilder SetFormat(ISegmentFormat format)
        {
            _format = format ?? throw new ArgumentNullException(nameof(format));
            return this;
        }

        /// <summary>When to start a new file. Default: every 10 minutes. Can be overridden per camera.</summary>
        public RecordingManagerBuilder SetRotationPolicy(IRotationPolicy rotationPolicy)
        {
            _rotationPolicy = rotationPolicy ?? throw new ArgumentNullException(nameof(rotationPolicy));
            return this;
        }

        /// <summary>Which old files to delete. Default: keep at least 1 GiB free on the volume.</summary>
        public RecordingManagerBuilder SetRetentionPolicy(IRetentionPolicy retentionPolicy)
        {
            _retentionPolicy = retentionPolicy ?? throw new ArgumentNullException(nameof(retentionPolicy));
            return this;
        }

        /// <summary>How often retention runs (it also runs after every completed segment). Default: 1 minute.</summary>
        public RecordingManagerBuilder SetRetentionInterval(TimeSpan interval)
        {
            _retentionInterval = RequirePositive(interval, nameof(interval));
            return this;
        }

        /// <summary>Back-off between reconnect attempts, doubling from minimum to maximum. Default: 1 s to 1 min.</summary>
        public RecordingManagerBuilder SetReconnectDelay(TimeSpan minimum, TimeSpan maximum)
        {
            RequirePositive(minimum, nameof(minimum));

            if (maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            _recorderOptions.MinReconnectDelay = minimum;
            _recorderOptions.MaxReconnectDelay = maximum;
            return this;
        }

        /// <summary>Reconnect when a connected camera sends no frame for this long. Default: 15 seconds.</summary>
        public RecordingManagerBuilder SetStallTimeout(TimeSpan timeout)
        {
            _recorderOptions.StallTimeout = RequirePositive(timeout, nameof(timeout));
            return this;
        }

        public RecordingManagerBuilder SetLoggerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            return this;
        }

        public RecordingManagerBuilder SetTimeProvider(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            return this;
        }

        public static RecordingManagerBuilder GetBuilder()
        {
            return new RecordingManagerBuilder();
        }

        #endregion

        #region Private Methods

        private static TimeSpan RequirePositive(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            return value;
        }

        #endregion
    }
}
