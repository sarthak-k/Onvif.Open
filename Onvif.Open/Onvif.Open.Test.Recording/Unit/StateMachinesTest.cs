// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Core.StateMachines;
using Onvif.Open.Recorder.Alerts;
using Onvif.Open.Recorder.Cases;
using Onvif.Open.Recorder.Exports;
using Onvif.Open.Recorder.Gateways;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    /// <summary>
    /// The state machines of the backend: the helper itself, and docs/STATE-MACHINES.md matching the tables in the code
    /// (run with UPDATE_STATE_DOCS=1 to rewrite the diagrams after changing a table).
    /// </summary>
    public sealed class StateMachinesTest
    {
        /// <summary>Every backend state machine; a new one is one line here (and a section in the doc).</summary>
        private static readonly (string Name, string Diagram)[] Machines =
        {
            Of(RecordingStates.Machine),
            Of(PluginStates.Machine),
            Of(ExportStatuses.Machine),
            Of(CaseStatuses.Machine),
            Of(AlertStates.Machine),
            Of(PairingStatus.Machine),
            Of(Onvif.Open.Recording.Implementation.Mp4.Mp4LiveStates.Machine),
            Of(Onvif.Open.Recorder.Hosting.SubStreamStates.Machine),
            Of(TunnelStates.Machine),
            Of(CloudGatewayStates.Machine),
        };

        private static (string, string) Of<T>(StateMachine<T> machine) where T : notnull => (machine.Name, machine.ToMermaid());

        [Fact]
        public void MovesOutsideTheTableShouldBeRefusedWithWhatIsAllowed()
        {
            var machine = ExportStatuses.Machine;
            Assert.Equal(ExportStatuses.Running, machine.Move(ExportStatuses.Queued, ExportStatuses.Running));
            Assert.True(machine.CanMove(ExportStatuses.Running, ExportStatuses.Running));

            var refused = Assert.Throws<InvalidOperationException>(() => machine.Move(ExportStatuses.Queued, ExportStatuses.Ready));
            Assert.Equal("Export: Queued cannot become Ready; it can become Running.", refused.Message);
            var final = Assert.Throws<InvalidOperationException>(() => machine.Move(ExportStatuses.Failed, ExportStatuses.Running));
            Assert.Equal("Export: Failed is final; it cannot become Running.", final.Message);

            Assert.Equal(new[] { ExportStatuses.Failed, ExportStatuses.Expired }, machine.Final);
            Assert.True(ExportStatuses.IsUnfinished(ExportStatuses.Queued) && ExportStatuses.IsUnfinished(ExportStatuses.Running));
            Assert.False(ExportStatuses.IsUnfinished(ExportStatuses.Ready) || ExportStatuses.IsUnfinished("Nonsense"));
        }

        [Fact]
        public void ARecordingSessionShouldFollowItsTable()
        {
            // What the recorder reports for a camera that drops once and is then stopped (see RecordingManagerTest).
            var lifecycle = new[]
            {
                RecordingState.Stopped, RecordingState.Connecting, RecordingState.Recording, RecordingState.Reconnecting,
                RecordingState.Connecting, RecordingState.Recording, RecordingState.Stopped
            };
            Assert.All(lifecycle.Zip(lifecycle.Skip(1)), step => Assert.True(RecordingStates.Machine.CanMove(step.First, step.Second), $"{step.First} -> {step.Second}"));
            Assert.False(RecordingStates.Machine.CanMove(RecordingState.Stopped, RecordingState.Recording));
            Assert.Empty(RecordingStates.Machine.Final);
        }

        [Fact]
        public void AlertsAndCasesShouldNameTheirStates()
        {
            Assert.Equal(AlertStates.Open, AlertStates.Of(new Onvif.Open.Recorder.Store.AlertRecord()));
            Assert.Equal(AlertStates.Acknowledged, AlertStates.Of(new Onvif.Open.Recorder.Store.AlertRecord { AcknowledgedAt = DateTime.UtcNow }));
            Assert.False(AlertStates.Machine.CanMove(AlertStates.Acknowledged, AlertStates.Open));
            Assert.True(CaseStatuses.Machine.CanMove(CaseStatuses.Closed, CaseStatuses.Open));
        }

        [Fact]
        public void TheDocShouldShowTheMachinesAsTheCodeHasThem()
        {
            var path = Path.Combine(RepositoryRoot(), "docs", "STATE-MACHINES.md");
            var doc = File.ReadAllText(path).Replace("\r\n", "\n");
            var updated = doc;
            var missing = new List<string>();

            foreach (var (name, diagram) in Machines)
            {
                var block = new Regex($"(<!-- machine: {Regex.Escape(name)} -->\\n```mermaid\\n)(.*?)(```\\n<!-- /machine -->)", RegexOptions.Singleline);
                if (!block.IsMatch(updated))
                {
                    missing.Add(name);
                    continue;
                }

                updated = block.Replace(updated, m => m.Groups[1].Value + diagram + m.Groups[3].Value);
            }

            Assert.True(missing.Count == 0, $"docs/STATE-MACHINES.md has no section for: {string.Join(", ", missing)}. Add `<!-- machine: Name -->` + a mermaid block + `<!-- /machine -->`.");
            if (updated != doc && Environment.GetEnvironmentVariable("UPDATE_STATE_DOCS") == "1")
            {
                File.WriteAllText(path, updated);
                return;
            }

            Assert.True(updated == doc, "docs/STATE-MACHINES.md is out of date with the state machines in the code: run the tests with UPDATE_STATE_DOCS=1.");
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Onvif.Open.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Onvif.Open.sln not found above the test binaries.");
        }
    }
}
