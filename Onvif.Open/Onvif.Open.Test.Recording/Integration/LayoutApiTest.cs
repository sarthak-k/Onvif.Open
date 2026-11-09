// Sarthak Killedar
// 2026-11-08

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Live view layouts: private to each user, shared ones managed by administrators.
    /// </summary>
    public sealed class LayoutApiTest : IDisposable
    {
        #region Constructor

        public LayoutApiTest()
        {
            _storage = new TempDirectory();
            _settings = new TestSettings();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
            });
            _admin = TestSettings.Authenticate(_factory.CreateClient());
        }

        #endregion

        #region Private Variables

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _admin;

        #endregion

        #region Private Methods

        /// <summary>A signed-in user with that role (who has already chosen their own password).</summary>
        private async Task<HttpClient> UserAsync(string userName, string role)
        {
            var created = await _admin.PostAsJsonAsync("/api/users", new { userName, password = "Temporary-Pass-1", role });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var client = _factory.CreateClient();
            var tokens = await TestSettings.SignInAsync(client, userName, "Temporary-Pass-1");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var changed = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = userName + "-Own-Pass-2" });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var accessToken = JsonDocument.Parse(await changed.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            return client;
        }

        private static async Task<JsonElement> CreateAsync(HttpClient client, object layout, HttpStatusCode expected = HttpStatusCode.Created)
        {
            var response = await client.PostAsJsonAsync("/api/layouts", layout);
            Assert.Equal(expected, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        }

        private static async Task<string[]> NamesAsync(HttpClient client)
        {
            return JsonDocument.Parse(await client.GetStringAsync("/api/layouts")).RootElement.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToArray();
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task LayoutsShouldBeSavedChangedAndDeleted()
        {
            var created = await CreateAsync(_admin, new { name = "Entrances", grid = "2x2", cameras = new[] { "front", null, "back", "" } });

            Assert.Equal("2x2", created.GetProperty("grid").GetString());
            Assert.Equal(new[] { "front", null, "back", null }, created.GetProperty("cameras").EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()));
            Assert.False(created.GetProperty("shared").GetBoolean());
            Assert.True(created.GetProperty("editable").GetBoolean());

            var id = created.GetProperty("id").GetString();
            var changed = await _admin.PutAsJsonAsync($"/api/layouts/{id}", new { name = "All", grid = "1+5", cameras = new[] { "back" } });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.Equal(new[] { "All" }, await NamesAsync(_admin));

            Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/layouts/{id}")).StatusCode);
            Assert.Empty(await NamesAsync(_admin));
            Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/layouts/{id}")).StatusCode);
        }

        [Fact]
        public async Task PrivateLayoutsShouldBeInvisibleToOthers()
        {
            using (var viewer = await UserAsync("vera", "Viewer"))
            {
                var mine = await CreateAsync(viewer, new { name = "Vera's", grid = "1", cameras = new[] { "front" } });
                var id = mine.GetProperty("id").GetString();

                Assert.Equal(new[] { "Vera's" }, await NamesAsync(viewer));
                Assert.Empty(await NamesAsync(_admin));

                // Not even administrators see or change someone's private layout.
                Assert.Equal(HttpStatusCode.NotFound, (await _admin.PutAsJsonAsync($"/api/layouts/{id}", new { name = "x", grid = "1", cameras = Array.Empty<string>() })).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/layouts/{id}")).StatusCode);
            }
        }

        [Fact]
        public async Task SharedLayoutsShouldBeSeenByAllAndManagedByAdministrators()
        {
            var shared = await CreateAsync(_admin, new { name = "Control room", grid = "3x3", cameras = new[] { "front" }, shared = true });
            var id = shared.GetProperty("id").GetString();
            Assert.True(shared.GetProperty("shared").GetBoolean());

            using (var viewer = await UserAsync("otto", "Operator"))
            {
                var seen = JsonDocument.Parse(await viewer.GetStringAsync("/api/layouts")).RootElement.EnumerateArray().Single();
                Assert.Equal("Control room", seen.GetProperty("name").GetString());
                Assert.False(seen.GetProperty("editable").GetBoolean());

                Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/layouts/{id}", new { name = "Mine now", grid = "1", cameras = Array.Empty<string>() })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/layouts/{id}")).StatusCode);
                await CreateAsync(viewer, new { name = "Sneaky", grid = "1", cameras = Array.Empty<string>(), shared = true }, HttpStatusCode.Forbidden);
            }

            Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/layouts/{id}")).StatusCode);
        }

        [Fact]
        public async Task InvalidLayoutsShouldBeRefused()
        {
            var errors = await CreateAsync(_admin, new { name = " ", grid = "<script>", cameras = new[] { "../etc" } }, HttpStatusCode.BadRequest);
            var messages = errors.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();

            Assert.Contains(messages, m => m.StartsWith("Name", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.StartsWith("Grid", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("../etc", StringComparison.Ordinal));

            await CreateAsync(_admin, new { name = "Too many", grid = "8x8", cameras = Enumerable.Repeat("cam", 65) }, HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task LayoutsShouldGoWithTheirUser()
        {
            string userId;
            using (var viewer = await UserAsync("leaving", "Viewer"))
            {
                await CreateAsync(viewer, new { name = "Gone soon", grid = "1", cameras = Array.Empty<string>() });
                userId = JsonDocument.Parse(await viewer.GetStringAsync("/api/auth/me")).RootElement.GetProperty("id").GetString();
            }

            Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/users/{userId}")).StatusCode);

            await using (var db = await ((Microsoft.EntityFrameworkCore.IDbContextFactory<Onvif.Open.Recorder.Store.RecorderDbContext>)_factory.Services.GetService(typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<Onvif.Open.Recorder.Store.RecorderDbContext>))).CreateDbContextAsync())
            {
                Assert.Empty(db.Layouts.ToList());
            }
        }

        [Fact]
        public async Task LayoutsShouldNeedASignedInUser()
        {
            using (var anonymous = _factory.CreateClient())
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/layouts")).StatusCode);
            }
        }

        public void Dispose()
        {
            _admin.Dispose();
            _factory.Dispose();
            _storage.Dispose();
            _settings.Dispose();
        }

        #endregion
    }
}
