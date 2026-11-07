// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Onvif.Open.Core.Storage
{
    /// <summary>
    /// What a storage provider gets to create its store: its settings and a logger.
    /// </summary>
    public sealed class SegmentStoreContext
    {
        #region Constructor

        public SegmentStoreContext(IReadOnlyDictionary<string, string> settings, ILogger logger = null)
        {
            _settings = settings == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
            Logger = logger ?? NullLogger.Instance;
        }

        #endregion

        #region Private Variables

        private readonly Dictionary<string, string> _settings;

        #endregion

        #region Public Variables

        /// <summary>Settings from configuration, case-insensitive; nested keys use ':' (e.g. "Credentials:AccessKey").</summary>
        public IReadOnlyDictionary<string, string> Settings => _settings;

        public ILogger Logger { get; }

        #endregion

        #region Public Methods

        /// <summary>The setting, or <paramref name="defaultValue"/> when it is missing or empty.</summary>
        public string Get(string name, string defaultValue = null)
        {
            return _settings.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : defaultValue;
        }

        /// <summary>The setting; throws a clear error when it is missing.</summary>
        public string Require(string name)
        {
            return Get(name) ?? throw new InvalidOperationException($"Storage setting '{name}' is required.");
        }

        public bool GetBoolean(string name, bool defaultValue = false)
        {
            var value = Get(name);

            if (value == null)
            {
                return defaultValue;
            }

            return bool.TryParse(value, out var result)
                ? result
                : throw new InvalidOperationException($"Storage setting '{name}' must be true or false, not '{value}'.");
        }

        #endregion
    }
}
