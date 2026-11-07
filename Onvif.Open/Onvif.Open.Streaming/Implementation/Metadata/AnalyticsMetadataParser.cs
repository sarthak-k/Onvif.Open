// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Onvif.Open.Core.Analytics;

namespace Onvif.Open.Streaming.Implementation.Metadata
{
    /// <summary>
    /// Parses ONVIF metadata documents (tt:MetadataStream) into <see cref="AnalyticsFrame"/>s.
    /// Element names are matched without namespace prefixes, because vendors use various ones.
    /// </summary>
    /// <remarks>
    /// Shapes use the ONVIF normalized coordinate system (-1..1, y up), optionally mapped by the frame's
    /// tt:Transformation; boxes are converted to image fractions (0..1 from the top left).
    /// </remarks>
    internal static class AnalyticsMetadataParser
    {
        #region Private Variables

        private const int MaxDocumentCharacters = 4 * 1024 * 1024;

        private static readonly XmlReaderSettings Settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDocumentCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        #endregion

        #region Public Methods

        /// <summary>The analytics frames in <paramref name="document"/>; empty for documents without (or with invalid) analytics.</summary>
        public static IReadOnlyList<AnalyticsFrame> Parse(string document)
        {
            if (string.IsNullOrWhiteSpace(document))
            {
                return Array.Empty<AnalyticsFrame>();
            }

            XElement root;
            try
            {
                using (var reader = XmlReader.Create(new StringReader(document), Settings))
                {
                    root = XElement.Load(reader);
                }
            }
            catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
            {
                return Array.Empty<AnalyticsFrame>();
            }

            return root.DescendantsAndSelf()
                .Where(e => e.Name.LocalName == "Frame" && e.Parent?.Name.LocalName == "VideoAnalytics")
                .Select(ToFrame)
                .ToList();
        }

        #endregion

        #region Private Methods

        private static AnalyticsFrame ToFrame(XElement frame)
        {
            var time = DateTimeOffset.TryParse((string)frame.Attribute("UtcTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            var transformation = Child(frame, "Transformation");
            var translate = Child(transformation, "Translate");
            var scale = Child(transformation, "Scale");
            var mapping = new Mapping(Number(translate, "x") ?? 0, Number(translate, "y") ?? 0, Number(scale, "x") ?? 1, Number(scale, "y") ?? 1);

            var objects = frame.Elements().Where(e => e.Name.LocalName == "Object").Select(o => ToObject(o, mapping)).ToList();
            return new AnalyticsFrame(time, (string)frame.Attribute("Source"), objects);
        }

        private static DetectedObject ToObject(XElement element, Mapping mapping)
        {
            var appearance = Child(element, "Appearance");
            var box = Child(Child(appearance, "Shape"), "BoundingBox");
            NormalizedBox boundingBox = null;

            if (box != null && Number(box, "left") is double left && Number(box, "top") is double top && Number(box, "right") is double right && Number(box, "bottom") is double bottom)
            {
                boundingBox = new NormalizedBox(mapping.X(left), mapping.Y(top), mapping.X(right), mapping.Y(bottom));
            }

            var classes = Classes(Child(appearance, "Class"))
                .OrderByDescending(c => c.Likelihood ?? 1.0)
                .ToList();

            return new DetectedObject((string)element.Attribute("ObjectId"), classes, boundingBox);
        }

        private static IEnumerable<ObjectClassification> Classes(XElement classElement)
        {
            if (classElement == null)
            {
                yield break;
            }

            foreach (var child in classElement.Elements())
            {
                switch (child.Name.LocalName)
                {
                    // ONVIF 2.x / Profile M: <tt:Type Likelihood="0.9">Human</tt:Type>
                    case "Type" when !string.IsNullOrWhiteSpace(child.Value):
                        yield return new ObjectClassification(child.Value.Trim(), Number(child, "Likelihood"));
                        break;

                    // ONVIF 1.x: <tt:ClassCandidate><tt:Type>Human</tt:Type><tt:Likelihood>0.8</tt:Likelihood></tt:ClassCandidate>
                    case "ClassCandidate":
                        var type = Child(child, "Type")?.Value?.Trim();
                        if (!string.IsNullOrEmpty(type))
                        {
                            yield return new ObjectClassification(type, ParseNumber(Child(child, "Likelihood")?.Value));
                        }

                        break;
                }
            }
        }

        private static XElement Child(XElement element, string localName)
        {
            return element?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        }

        private static double? Number(XElement element, string attribute)
        {
            return ParseNumber((string)element?.Attribute(attribute));
        }

        private static double? ParseNumber(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number)
                ? number
                : (double?)null;
        }

        #endregion

        /// <summary>Frame coordinates → ONVIF normalized (-1..1, y up) → image fractions (0..1, y down).</summary>
        private readonly struct Mapping
        {
            private readonly double _translateX;
            private readonly double _translateY;
            private readonly double _scaleX;
            private readonly double _scaleY;

            public Mapping(double translateX, double translateY, double scaleX, double scaleY)
            {
                _translateX = translateX;
                _translateY = translateY;
                _scaleX = scaleX;
                _scaleY = scaleY;
            }

            public double X(double x) => ((x * _scaleX) + _translateX + 1) / 2;

            public double Y(double y) => (1 - ((y * _scaleY) + _translateY)) / 2;
        }
    }
}
