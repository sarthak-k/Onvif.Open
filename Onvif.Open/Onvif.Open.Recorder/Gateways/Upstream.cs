// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// What the control plane takes from a gateway: changes about cameras that gateway records (anything else is
    /// rejected), with the organization and site from the control plane's own records, never the gateway's word.
    /// </summary>
    public static class Upstream
    {
        private const int MaxTitle = 200;
        private const int MaxData = 8 * 1024;

        public static (List<EventRecord> Adds, List<(string Id, string CameraId, DateTime Start, DateTime End)> Closes, int Rejected) Accept(Gateway gateway, IEnumerable<UpstreamEvent> changes, CameraDirectory directory)
        {
            var adds = new List<EventRecord>();
            var closes = new List<(string, string, DateTime, DateTime)>();
            var rejected = 0;

            foreach (var change in changes ?? Enumerable.Empty<UpstreamEvent>())
            {
                var camera = directory.Find(change?.CameraId);
                if (camera == null || !string.Equals(camera.GatewayId, gateway.Id, StringComparison.OrdinalIgnoreCase) || camera.OrganizationId != gateway.OrganizationId
                    || string.IsNullOrEmpty(change.Id) || change.Id.Length > 36)
                {
                    rejected++;
                    continue;
                }

                if (change.Kind == UpstreamEvent.Started && change.Event != null)
                {
                    var record = change.Event;
                    adds.Add(new EventRecord
                    {
                        Id = change.Id,
                        OrganizationId = camera.OrganizationId,
                        SiteId = camera.SiteId,
                        CameraId = camera.Id,
                        Category = EventCategories.All.Contains(record.Category) ? record.Category : EventCategories.Other,
                        Severity = record.Severity is "Info" or "Warning" or "Critical" ? record.Severity : "Info",
                        Source = Cut(record.Source, 64),
                        Title = Cut(record.Title, MaxTitle),
                        Topic = Cut(record.Topic, 256),
                        StartTime = DateTime.SpecifyKind(record.StartTime, DateTimeKind.Utc),
                        EndTime = record.EndTime is DateTime end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : null,
                        Data = record.Data?.Length > MaxData ? null : record.Data
                    });
                }
                else if (change.Kind == UpstreamEvent.Ended && change.Start is DateTime start && change.End is DateTime ended)
                {
                    closes.Add((change.Id, camera.Id, DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(ended, DateTimeKind.Utc)));
                }
                else
                {
                    rejected++;
                }
            }

            return (adds, closes, rejected);
        }

        private static string Cut(string text, int length) => text == null || text.Length <= length ? text : text.Substring(0, length);
    }
}
