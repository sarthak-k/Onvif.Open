// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// The recorder with "Recording:Archive" configured: a segment on the local disk is moved to the archive
    /// and can still be listed, downloaded and deleted through the API.
    /// </summary>
    public sealed class RecorderArchiveTest : IDisposable
    {
        #region Constructor

        public RecorderArchiveTest()
        {
            _storage = new TempDirectory();
            _archive = new TempDirectory();

            var day = Path.Combine(_storage.Path, "cam1", "2026-11-01");
            Directory.CreateDirectory(day);
            File.WriteAllBytes(Path.Combine(day, SegmentName), Enumerable.Repeat((byte)0x47, 188 * 3).ToArray());

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Recording:Retention:MinFreeSpaceGB", "0");
                builder.UseSetting("Recording:RetentionInterval", "00:00:00.200");
                builder.UseSetting("Recording:Archive:Provider", "FileSystem");
                builder.UseSetting("Recording:Archive:Prefix", "site-a");
                builder.UseSetting("Recording:Archive:Settings:Path", _archive.Path);
                builder.UseSetting("Recording:Cameras:0:Id", "cam1");
                builder.UseSetting("Recording:Cameras:0:RtspUrl", "rtsp://127.0.0.1:1/stream");
            });

            _client = _factory.CreateClient();
        }

        #endregion

        #region Private Variables

        private const string SegmentName = "cam1_20261101T080000000Z.ts";

        private readonly TempDirectory _storage;
        private readonly TempDirectory _archive;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        #endregion

        #region Public Methods

        [Fact]
        public async Task SegmentsShouldBeArchivedAndServedFromTheArchive()
        {
            var archived = Path.Combine(_archive.Path, "site-a", "cam1", "2026-11-01", SegmentName);
            await Eventually.True(() => File.Exists(archived) && !File.Exists(Path.Combine(_storage.Path, "cam1", "2026-11-01", SegmentName)), "the segment is moved to the archive");

            var segments = JsonDocument.Parse(await _client.GetStringAsync("/api/cameras/cam1/segments")).RootElement;
            var segment = segments.EnumerateArray().Single();
            Assert.True(segment.GetProperty("isArchived").GetBoolean());
            Assert.False(segment.GetProperty("isLocal").GetBoolean());

            var download = await _client.GetAsync("/api/cameras/cam1/segments/" + SegmentName);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(188 * 3, (await download.Content.ReadAsByteArrayAsync()).Length);

            var storage = JsonDocument.Parse(await _client.GetStringAsync("/api/storage")).RootElement.GetProperty("archive");
            Assert.Equal("FileSystem", storage.GetProperty("provider").GetString());
            Assert.True(storage.GetProperty("isAvailable").GetBoolean());
            Assert.Equal(0, storage.GetProperty("pendingUploads").GetInt32());

            Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/cameras/cam1/segments/" + SegmentName)).StatusCode);
            Assert.False(File.Exists(archived));
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/cam1/segments/" + SegmentName)).StatusCode);
        }

        [Fact]
        public void AMissingStoragePluginShouldFailAtStartup()
        {
            using (var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Recording:Archive:Provider", "Onvif.Open.Storage.Missing");
                builder.UseSetting("Plugins:Directory", _archive.Path);
            }))
            {
                var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
                Assert.Contains("Onvif.Open.Storage.Missing", error.ToString());
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
            _storage.Dispose();
            _archive.Dispose();
        }

        #endregion
    }
}
