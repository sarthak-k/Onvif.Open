// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Core.Analytics
{
    /// <summary>
    /// What the camera's own analytics detected in one video frame (ONVIF metadata stream, Profile M).
    /// </summary>
    public sealed class AnalyticsFrame
    {
        #region Constructor

        public AnalyticsFrame(DateTimeOffset utcTime, string source, IReadOnlyList<DetectedObject> objects, string cameraId = null)
        {
            UtcTime = utcTime;
            Source = source;
            Objects = objects ?? Array.Empty<DetectedObject>();
            CameraId = cameraId;
        }

        #endregion

        #region Public Variables

        /// <summary>Camera id in the recorder (null outside the recorder).</summary>
        public string CameraId { get; }

        /// <summary>Time of the analysed video frame, in the camera's clock.</summary>
        public DateTimeOffset UtcTime { get; }

        /// <summary>Video source token the analytics ran on, when the camera reports it.</summary>
        public string Source { get; }

        public IReadOnlyList<DetectedObject> Objects { get; }

        #endregion

        #region Public Methods

        /// <summary>True when an object of <paramref name="objectClass"/> (e.g. "Human", "Vehicle"; case-insensitive) was detected with at least <paramref name="minLikelihood"/>.</summary>
        public bool Contains(string objectClass, double minLikelihood = 0)
        {
            return Objects.Any(o => o.Is(objectClass, minLikelihood));
        }

        public AnalyticsFrame WithCameraId(string cameraId)
        {
            return new AnalyticsFrame(UtcTime, Source, Objects, cameraId);
        }

        #endregion
    }

    /// <summary>
    /// One object found by the camera's analytics: its classification and where it is in the picture.
    /// </summary>
    public sealed class DetectedObject
    {
        #region Constructor

        public DetectedObject(string objectId, IReadOnlyList<ObjectClassification> classes, NormalizedBox boundingBox)
        {
            ObjectId = objectId;
            Classes = classes ?? Array.Empty<ObjectClassification>();
            BoundingBox = boundingBox;
        }

        #endregion

        #region Public Variables

        /// <summary>Tracking id: the same object keeps its id across frames.</summary>
        public string ObjectId { get; }

        /// <summary>Every classification the camera reported, most likely first.</summary>
        public IReadOnlyList<ObjectClassification> Classes { get; }

        /// <summary>Most likely class, e.g. "Human", "Vehicle", "Face"; null when not classified.</summary>
        public string Class => Classes.Count == 0 ? null : Classes[0].Type;

        public double? Likelihood => Classes.Count == 0 ? null : Classes[0].Likelihood;

        /// <summary>Position in the picture, or null when the camera did not report a shape.</summary>
        public NormalizedBox BoundingBox { get; }

        #endregion

        #region Public Methods

        /// <summary>True when any classification matches <paramref name="objectClass"/> (case-insensitive) with at least <paramref name="minLikelihood"/> (an unknown likelihood counts as certain).</summary>
        public bool Is(string objectClass, double minLikelihood = 0)
        {
            return Classes.Any(c => string.Equals(c.Type, objectClass, StringComparison.OrdinalIgnoreCase) && (c.Likelihood ?? 1.0) >= minLikelihood);
        }

        #endregion
    }

    /// <summary>
    /// A class an object was given, with the camera's confidence (0..1) when reported.
    /// </summary>
    public sealed class ObjectClassification
    {
        #region Constructor

        public ObjectClassification(string type, double? likelihood)
        {
            Type = type;
            Likelihood = likelihood;
        }

        #endregion

        #region Public Variables

        public string Type { get; }
        public double? Likelihood { get; }

        #endregion
    }

    /// <summary>
    /// A rectangle in image fractions: 0..1 from the left and from the top of the picture, whatever its resolution.
    /// </summary>
    public sealed class NormalizedBox
    {
        #region Constructor

        public NormalizedBox(double left, double top, double right, double bottom)
        {
            Left = Clamp(Math.Min(left, right));
            Right = Clamp(Math.Max(left, right));
            Top = Clamp(Math.Min(top, bottom));
            Bottom = Clamp(Math.Max(top, bottom));
        }

        #endregion

        #region Public Variables

        public double Left { get; }
        public double Top { get; }
        public double Right { get; }
        public double Bottom { get; }
        public double Width => Right - Left;
        public double Height => Bottom - Top;

        #endregion

        #region Public Methods

        /// <summary>The box in pixels of a picture of the given size.</summary>
        public (int X, int Y, int Width, int Height) ToPixels(int imageWidth, int imageHeight)
        {
            var x = (int)Math.Round(Left * imageWidth);
            var y = (int)Math.Round(Top * imageHeight);
            return (x, y, (int)Math.Round(Right * imageWidth) - x, (int)Math.Round(Bottom * imageHeight) - y);
        }

        public override string ToString()
        {
            return FormattableString.Invariant($"[{Left:0.###},{Top:0.###} - {Right:0.###},{Bottom:0.###}]");
        }

        #endregion

        #region Private Methods

        private static double Clamp(double value)
        {
            return double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value));
        }

        #endregion
    }
}
