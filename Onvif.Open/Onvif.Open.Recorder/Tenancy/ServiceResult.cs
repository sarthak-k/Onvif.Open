// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Tenancy
{
    public enum ServiceStatus
    {
        Ok,
        NotFound,
        Invalid,
        Conflict,
        Forbidden
    }

    /// <summary>The outcome of a change: the value, or why it was refused.</summary>
    public sealed class ServiceResult<T>
    {
        private ServiceResult(ServiceStatus status, T value, IReadOnlyList<string> errors)
        {
            Status = status;
            Value = value;
            Errors = errors ?? Array.Empty<string>();
        }

        public ServiceStatus Status { get; }
        public T Value { get; }
        public IReadOnlyList<string> Errors { get; }

        public static ServiceResult<T> Ok(T value) => new ServiceResult<T>(ServiceStatus.Ok, value, null);
        public static ServiceResult<T> NotFound() => new ServiceResult<T>(ServiceStatus.NotFound, default, null);
        public static ServiceResult<T> Invalid(IEnumerable<string> errors) => new ServiceResult<T>(ServiceStatus.Invalid, default, errors.ToList());
        public static ServiceResult<T> Conflict(string error) => new ServiceResult<T>(ServiceStatus.Conflict, default, new[] { error });
        public static ServiceResult<T> Forbidden(string error) => new ServiceResult<T>(ServiceStatus.Forbidden, default, new[] { error });
    }
}
