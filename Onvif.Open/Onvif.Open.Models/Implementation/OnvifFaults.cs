// Sarthak Killedar
// 2026-11-07

using System;
using System.ServiceModel;
using System.ServiceModel.Security;

namespace Onvif.Open.Core.Implementation
{
    /// <summary>
    /// Classifies failures returned by ONVIF devices.
    /// </summary>
    public static class OnvifFaults
    {
        #region Private Variables

        private static readonly string[] AuthenticationSubcodes = { "NotAuthorized", "FailedAuthentication", "InvalidSecurity", "InvalidSecurityToken", "MessageExpired" };
        private static readonly string[] InvalidArgumentSubcodes = { "InvalidArgVal", "InvalidArgs", "InvalidArgValue", "NoRecording", "NoProfile", "NoToken" };

        #endregion

        #region Public Methods

        /// <summary>
        /// True when the device rejected the request's credentials (wrong password, or a WS-Security
        /// timestamp outside the device's tolerance). Devices report this as an ONVIF/WS-Security fault,
        /// an HTTP 401, or an HTTP 400 carrying the fault.
        /// </summary>
        public static bool IsAuthenticationFailure(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                switch (current)
                {
                    case MessageSecurityException _:
                        return true;
                    case FaultException fault when HasAuthenticationCode(fault.Code) || ContainsAuthenticationText(fault.Message):
                        return true;
                    case ProtocolException protocol when protocol.Message.Contains("(400)", StringComparison.Ordinal) || protocol.Message.Contains("(401)", StringComparison.Ordinal):
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the device rejected an argument, e.g. an unknown token (ter:InvalidArgVal, ter:NoRecording, ...).
        /// </summary>
        public static bool IsInvalidArgument(Exception exception)
        {
            return exception is FaultException fault && !IsAuthenticationFailure(exception) && HasCode(fault.Code, InvalidArgumentSubcodes);
        }

        #endregion

        #region Private Methods

        private static bool HasAuthenticationCode(FaultCode code)
        {
            return HasCode(code, AuthenticationSubcodes);
        }

        private static bool HasCode(FaultCode code, string[] names)
        {
            for (var current = code; current != null; current = current.SubCode)
            {
                foreach (var name in names)
                {
                    if (string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ContainsAuthenticationText(string message)
        {
            return message != null &&
                   (message.IndexOf("not authorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("authentication", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion
    }
}
