// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Test.Camera.Fakes
{
    /// <summary>
    /// In-process ONVIF device answering GetCapabilities, GetProfiles, GetStreamUri and GetSnapshotUri with SOAP 1.2.
    /// </summary>
    internal sealed class FakeOnvifServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Task _serve;

        public FakeOnvifServer(Uri rtspUri)
        {
            RtspUri = rtspUri;
            Port = GetFreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/onvif/");
            _listener.Start();
            _serve = Task.Run(ServeAsync);
        }

        public int Port { get; }
        public Uri RtspUri { get; }
        public Uri DeviceServiceUri => new Uri($"http://127.0.0.1:{Port}/onvif/device_service");
        public Uri MediaServiceUri => new Uri($"http://127.0.0.1:{Port}/onvif/media_service");
        public Uri EventServiceUri => new Uri($"http://127.0.0.1:{Port}/onvif/event_service");

        /// <summary>Notification messages (wsnt:NotificationMessage XML) returned by the next PullMessages calls.</summary>
        public ConcurrentQueue<string> PendingNotifications { get; } = new ConcurrentQueue<string>();

        public int SubscriptionsCreated => _subscriptionCount;
        public int Unsubscribes { get; private set; }
        public int Renewals { get; private set; }

        /// <summary>Makes the next PullMessages fail as if the subscription had expired.</summary>
        public bool ExpireSubscription { get; set; }

        /// <summary>Bodies (with headers) of the requests sent to subscription addresses.</summary>
        public ConcurrentQueue<string> SubscriptionRequests { get; } = new ConcurrentQueue<string>();

        private int _subscriptionCount;

        public Uri PtzServiceUri => new Uri($"http://127.0.0.1:{Port}/onvif/ptz_service");
        public Uri Media2ServiceUri => new Uri($"http://127.0.0.1:{Port}/onvif/media2_service");

        /// <summary>When true the device lists and implements Media2 (ver20).</summary>
        public bool HasMedia2 { get; set; }

        /// <summary>When false GetServices returns a fault, like pre-2.0 devices.</summary>
        public bool SupportsGetServices { get; set; } = true;

        public string LastMedia2Protocol { get; private set; }
        public int Media2Requests { get; private set; }

        /// <summary>When false the device reports no PTZ capability (a fixed camera).</summary>
        public bool HasPtz { get; set; } = true;

        public double PtzPan { get; set; }
        public double PtzTilt { get; set; }
        public double PtzZoom { get; set; }
        public bool PtzMoving { get; private set; }
        public string LastPtzRequest { get; private set; }
        public ConcurrentQueue<string> PtzOperations { get; } = new ConcurrentQueue<string>();
        public ConcurrentDictionary<string, (string Name, double Pan, double Tilt, double Zoom)> PtzPresets { get; } = new ConcurrentDictionary<string, (string, double, double, double)>();
        private (double Pan, double Tilt, double Zoom) _ptzHome;
        private int _presetCount;
        public Uri SnapshotUri => new Uri($"http://127.0.0.1:{Port}/onvif/snapshot.jpg");
        public static readonly byte[] SnapshotJpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9 };
        public ConcurrentQueue<string> RequestBodies { get; } = new ConcurrentQueue<string>();
        public string LastSnapshotAuthorization { get; private set; }

        /// <summary>Device clock minus real time.</summary>
        public TimeSpan ClockOffset { get; set; }

        /// <summary>Reject tokens whose wsu:Created is further than 5 seconds from the device clock, like real devices.</summary>
        public bool RequireFreshTokens { get; set; }

        public int RejectedRequests { get; private set; }

        /// <summary>Reject every secured request (simulates a wrong password).</summary>
        public bool RejectAllTokens { get; set; }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == SnapshotUri.AbsolutePath)
                {
                    await ServeSnapshotAsync(context).ConfigureAwait(false);
                    continue;
                }

                string body;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                RequestBodies.Enqueue(body);

                if (context.Request.Url.AbsolutePath.StartsWith("/onvif/subscription/", StringComparison.Ordinal))
                {
                    SubscriptionRequests.Enqueue(body);
                }

                if (body.Contains("PullMessages", StringComparison.Ordinal) && !ExpireSubscription && PendingNotifications.IsEmpty)
                {
                    // Long poll: hold the request briefly when nothing is pending, like a device does.
                    await Task.Delay(100).ConfigureAwait(false);
                }

                var secured = !body.Contains("GetSystemDateAndTime", StringComparison.Ordinal);
                var rejected = secured && (RejectAllTokens || (RequireFreshTokens && !IsTokenFresh(body)));
                var expired = !rejected && ExpireSubscription && body.Contains("PullMessages", StringComparison.Ordinal);
                if (expired)
                {
                    ExpireSubscription = false;
                }

                var response = Envelope(rejected ? NotAuthorizedFault() : expired ? SubscriptionExpiredFault() : CreateBody(body));
                var bytes = Encoding.UTF8.GetBytes(response);

                if (rejected)
                {
                    RejectedRequests++;
                    context.Response.StatusCode = 400;
                }
                else if (expired)
                {
                    context.Response.StatusCode = 500;
                }

                context.Response.ContentType = "application/soap+xml; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        private async Task ServeSnapshotAsync(HttpListenerContext context)
        {
            var authorization = context.Request.Headers["Authorization"];

            // Require HTTP Basic, like many cameras do for snapshots.
            if (authorization == null)
            {
                context.Response.StatusCode = 401;
                context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"camera\"");
                context.Response.Close();
                return;
            }

            LastSnapshotAuthorization = authorization;
            context.Response.ContentType = "image/jpeg";
            context.Response.ContentLength64 = SnapshotJpeg.Length;
            await context.Response.OutputStream.WriteAsync(SnapshotJpeg, 0, SnapshotJpeg.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private bool IsTokenFresh(string body)
        {
            var match = System.Text.RegularExpressions.Regex.Match(body, "<[^>]*Created[^>]*>([^<]+)<");
            if (!match.Success || !DateTime.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var created))
            {
                return false;
            }

            var deviceNow = DateTime.UtcNow + ClockOffset;
            return (created - deviceNow).Duration() <= TimeSpan.FromSeconds(5);
        }

        private static string NotAuthorizedFault()
        {
            return "<s:Fault><s:Code><s:Value>s:Sender</s:Value><s:Subcode><s:Value xmlns:ter=\"http://www.onvif.org/ver10/error\">ter:NotAuthorized</s:Value></s:Subcode></s:Code>" +
                   "<s:Reason><s:Text xml:lang=\"en\">Sender not Authorized</s:Text></s:Reason></s:Fault>";
        }

        private string SystemDateAndTime()
        {
            var now = DateTime.UtcNow + ClockOffset;
            return "<tds:GetSystemDateAndTimeResponse><tds:SystemDateAndTime>" +
                   "<tt:DateTimeType>NTP</tt:DateTimeType><tt:DaylightSavings>false</tt:DaylightSavings><tt:TimeZone><tt:TZ>UTC0</tt:TZ></tt:TimeZone>" +
                   $"<tt:UTCDateTime><tt:Time><tt:Hour>{now.Hour}</tt:Hour><tt:Minute>{now.Minute}</tt:Minute><tt:Second>{now.Second}</tt:Second></tt:Time>" +
                   $"<tt:Date><tt:Year>{now.Year}</tt:Year><tt:Month>{now.Month}</tt:Month><tt:Day>{now.Day}</tt:Day></tt:Date></tt:UTCDateTime>" +
                   $"<tt:LocalDateTime><tt:Time><tt:Hour>{now.Hour}</tt:Hour><tt:Minute>{now.Minute}</tt:Minute><tt:Second>{now.Second}</tt:Second></tt:Time>" +
                   $"<tt:Date><tt:Year>{now.Year}</tt:Year><tt:Month>{now.Month}</tt:Month><tt:Day>{now.Day}</tt:Day></tt:Date></tt:LocalDateTime>" +
                   "</tds:SystemDateAndTime></tds:GetSystemDateAndTimeResponse>";
        }

        private static string SubscriptionExpiredFault()
        {
            return "<s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en\">Subscription expired</s:Text></s:Reason></s:Fault>";
        }

        public static string MotionNotification(bool isMotion, string operation = "Changed")
        {
            return "<wsnt:NotificationMessage xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\" xmlns:tns1=\"http://www.onvif.org/ver10/topics\">" +
                   "<wsnt:Topic Dialect=\"http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet\">tns1:RuleEngine/CellMotionDetector/Motion</wsnt:Topic>" +
                   $"<wsnt:Message><tt:Message UtcTime=\"2026-11-07T10:00:00Z\" PropertyOperation=\"{operation}\">" +
                   "<tt:Source><tt:SimpleItem Name=\"VideoSourceConfigurationToken\" Value=\"VSC_1\"/><tt:SimpleItem Name=\"Rule\" Value=\"MyMotionDetectorRule\"/></tt:Source>" +
                   $"<tt:Data><tt:SimpleItem Name=\"IsMotion\" Value=\"{(isMotion ? "true" : "false")}\"/></tt:Data>" +
                   "</tt:Message></wsnt:Message></wsnt:NotificationMessage>";
        }

        private string EventResponse(string request)
        {
            if (request.Contains("CreatePullPointSubscription", StringComparison.Ordinal))
            {
                var id = Interlocked.Increment(ref _subscriptionCount);
                return "<tev:CreatePullPointSubscriptionResponse xmlns:tev=\"http://www.onvif.org/ver10/events/wsdl\" xmlns:wsa5=\"http://www.w3.org/2005/08/addressing\" xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\">" +
                       $"<tev:SubscriptionReference><wsa5:Address>http://127.0.0.1:{Port}/onvif/subscription/{id}</wsa5:Address>" +
                       $"<wsa5:ReferenceParameters><dom0:SubscriptionId xmlns:dom0=\"http://www.axis.com/2009/event\">sub-{id}</dom0:SubscriptionId></wsa5:ReferenceParameters></tev:SubscriptionReference>" +
                       "<wsnt:CurrentTime>2026-11-07T10:00:00Z</wsnt:CurrentTime><wsnt:TerminationTime>2026-11-07T10:01:00Z</wsnt:TerminationTime>" +
                       "</tev:CreatePullPointSubscriptionResponse>";
            }

            if (request.Contains("PullMessages", StringComparison.Ordinal))
            {
                var builder = new StringBuilder("<tev:PullMessagesResponse xmlns:tev=\"http://www.onvif.org/ver10/events/wsdl\" xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\">" +
                                                "<tev:CurrentTime>2026-11-07T10:00:00Z</tev:CurrentTime><tev:TerminationTime>2026-11-07T10:01:00Z</tev:TerminationTime>");
                while (PendingNotifications.TryDequeue(out var notification))
                {
                    builder.Append(notification);
                }

                return builder.Append("</tev:PullMessagesResponse>").ToString();
            }

            if (request.Contains("Renew", StringComparison.Ordinal))
            {
                Renewals++;
                return "<wsnt:RenewResponse xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\"><wsnt:TerminationTime>2026-11-07T10:02:00Z</wsnt:TerminationTime></wsnt:RenewResponse>";
            }

            if (request.Contains("Unsubscribe", StringComparison.Ordinal))
            {
                Unsubscribes++;
                return "<wsnt:UnsubscribeResponse xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\"/>";
            }

            return null;
        }

        private string PtzResponse(string request)
        {
            if (!request.Contains("http://www.onvif.org/ver20/ptz/wsdl", StringComparison.Ordinal))
            {
                return null;
            }

            var operation = System.Text.RegularExpressions.Regex.Match(request, "<(?:\\w+:)?(ContinuousMove|RelativeMove|AbsoluteMove|Stop|GetStatus|GetPresets|GotoPreset|SetPreset|RemovePreset|GotoHomePosition|SetHomePosition)[ >/]").Groups[1].Value;
            LastPtzRequest = request;
            PtzOperations.Enqueue(operation);

            double Attribute(string element, string name, double fallback)
            {
                var match = System.Text.RegularExpressions.Regex.Match(request, $"<(?:\\w+:)?{element}[^>]*\\s{name}=\"([^\"]+)\"");
                return match.Success ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
            }

            string Element(string name)
            {
                var match = System.Text.RegularExpressions.Regex.Match(request, $"<(?:\\w+:)?{name}[^>]*>([^<]*)<");
                return match.Success ? match.Groups[1].Value : null;
            }

            string Response(string name, string body = "") =>
                $"<tptz:{name}Response xmlns:tptz=\"http://www.onvif.org/ver20/ptz/wsdl\">{body}</tptz:{name}Response>";

            string Vector(string name, double pan, double tilt, double zoom) =>
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "<tt:{0}><tt:PanTilt x=\"{1}\" y=\"{2}\"/><tt:Zoom x=\"{3}\"/></tt:{0}>", name, pan, tilt, zoom);

            switch (operation)
            {
                case "ContinuousMove":
                    PtzMoving = true;
                    return Response(operation);
                case "Stop":
                    PtzMoving = false;
                    return Response(operation);
                case "AbsoluteMove":
                    PtzPan = Attribute("PanTilt", "x", PtzPan);
                    PtzTilt = Attribute("PanTilt", "y", PtzTilt);
                    PtzZoom = Attribute("Zoom", "x", PtzZoom);
                    return Response(operation);
                case "RelativeMove":
                    PtzPan += Attribute("PanTilt", "x", 0);
                    PtzTilt += Attribute("PanTilt", "y", 0);
                    PtzZoom += Attribute("Zoom", "x", 0);
                    return Response(operation);
                case "GetStatus":
                    var state = PtzMoving ? "MOVING" : "IDLE";
                    return Response(operation, $"<tptz:PTZStatus>{Vector("Position", PtzPan, PtzTilt, PtzZoom)}" +
                                               $"<tt:MoveStatus><tt:PanTilt>{state}</tt:PanTilt><tt:Zoom>IDLE</tt:Zoom></tt:MoveStatus><tt:UtcTime>2026-11-07T10:00:00Z</tt:UtcTime></tptz:PTZStatus>");
                case "GetPresets":
                    var presets = new StringBuilder();
                    foreach (var preset in PtzPresets.OrderBy(p => p.Key))
                    {
                        presets.Append($"<tptz:Preset token=\"{preset.Key}\"><tt:Name>{preset.Value.Name}</tt:Name>{Vector("PTZPosition", preset.Value.Pan, preset.Value.Tilt, preset.Value.Zoom)}</tptz:Preset>");
                    }

                    return Response(operation, presets.ToString());
                case "SetPreset":
                    var token = Element("PresetToken") ?? $"preset-{Interlocked.Increment(ref _presetCount)}";
                    PtzPresets[token] = (Element("PresetName") ?? token, PtzPan, PtzTilt, PtzZoom);
                    return Response(operation, $"<tptz:PresetToken>{token}</tptz:PresetToken>");
                case "GotoPreset":
                    var target = PtzPresets[Element("PresetToken")];
                    (PtzPan, PtzTilt, PtzZoom) = (target.Pan, target.Tilt, target.Zoom);
                    return Response(operation);
                case "RemovePreset":
                    PtzPresets.TryRemove(Element("PresetToken"), out _);
                    return Response(operation);
                case "SetHomePosition":
                    _ptzHome = (PtzPan, PtzTilt, PtzZoom);
                    return Response(operation);
                case "GotoHomePosition":
                    (PtzPan, PtzTilt, PtzZoom) = _ptzHome;
                    return Response(operation);
                default:
                    return null;
            }
        }

        private string ServicesResponse()
        {
            string Service(string ns, Uri address) =>
                $"<tds:Service><tds:Namespace>{ns}</tds:Namespace><tds:XAddr>{address}</tds:XAddr><tds:Version><tt:Major>2</tt:Major><tt:Minor>60</tt:Minor></tds:Version></tds:Service>";

            return "<tds:GetServicesResponse>" +
                   Service("http://www.onvif.org/ver10/device/wsdl", DeviceServiceUri) +
                   Service("http://www.onvif.org/ver10/media/wsdl", MediaServiceUri) +
                   (HasMedia2 ? Service("http://www.onvif.org/ver20/media/wsdl", Media2ServiceUri) : string.Empty) +
                   Service("http://www.onvif.org/ver10/events/wsdl", EventServiceUri) +
                   (HasPtz ? Service("http://www.onvif.org/ver20/ptz/wsdl", PtzServiceUri) : string.Empty) +
                   "</tds:GetServicesResponse>";
        }

        private string Media2Response(string request)
        {
            if (!request.Contains("http://www.onvif.org/ver20/media/wsdl", StringComparison.Ordinal))
            {
                return null;
            }

            Media2Requests++;

            if (!HasMedia2)
            {
                return "<s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en\">Action not supported</s:Text></s:Reason></s:Fault>";
            }

            const string Ns = "xmlns:tr2=\"http://www.onvif.org/ver20/media/wsdl\"";

            if (request.Contains("GetProfiles", StringComparison.Ordinal))
            {
                string Profile(string token, string name, string encoding, int width, int height, string fps, bool ptz) =>
                    $"<tr2:Profiles token=\"{token}\" fixed=\"true\"><tr2:Name>{name}</tr2:Name><tr2:Configurations>" +
                    "<tr2:VideoSource token=\"VSC_1\"><tt:Name>VSC</tt:Name><tt:UseCount>2</tt:UseCount><tt:SourceToken>VS_1</tt:SourceToken><tt:Bounds x=\"0\" y=\"0\" width=\"3840\" height=\"2160\"/></tr2:VideoSource>" +
                    $"<tr2:VideoEncoder token=\"VE_{token}\" GovLength=\"50\" Profile=\"Main\"><tt:Name>VE</tt:Name><tt:UseCount>1</tt:UseCount><tt:Encoding>{encoding}</tt:Encoding>" +
                    $"<tt:Resolution><tt:Width>{width}</tt:Width><tt:Height>{height}</tt:Height></tt:Resolution>" +
                    $"<tt:RateControl ConstantBitRate=\"false\"><tt:FrameRateLimit>{fps}</tt:FrameRateLimit><tt:BitrateLimit>8192</tt:BitrateLimit></tt:RateControl><tt:Quality>5</tt:Quality></tr2:VideoEncoder>" +
                    (ptz ? "<tr2:PTZ token=\"PTZ_2\"><tt:Name>PTZ</tt:Name><tt:UseCount>1</tt:UseCount><tt:NodeToken>Node_1</tt:NodeToken></tr2:PTZ>" : string.Empty) +
                    "</tr2:Configurations></tr2:Profiles>";

                return $"<tr2:GetProfilesResponse {Ns}>" +
                       Profile("M2_Main", "main-h265", "H265", 3840, 2160, "29.97", ptz: false) +
                       Profile("M2_Sub", "sub-h264", "H264", 640, 360, "12.5", ptz: true) +
                       "</tr2:GetProfilesResponse>";
            }

            if (request.Contains("GetStreamUri", StringComparison.Ordinal))
            {
                LastMedia2Protocol = System.Text.RegularExpressions.Regex.Match(request, "<(?:\\w+:)?Protocol[^>]*>([^<]*)<").Groups[1].Value;
                return $"<tr2:GetStreamUriResponse {Ns}><tr2:Uri>{RtspUri}</tr2:Uri></tr2:GetStreamUriResponse>";
            }

            if (request.Contains("GetSnapshotUri", StringComparison.Ordinal))
            {
                return $"<tr2:GetSnapshotUriResponse {Ns}><tr2:Uri>{SnapshotUri}</tr2:Uri></tr2:GetSnapshotUriResponse>";
            }

            return null;
        }

        private string CreateBody(string request)
        {
            if (request.Contains("GetServices", StringComparison.Ordinal))
            {
                return SupportsGetServices
                    ? ServicesResponse()
                    : "<s:Fault><s:Code><s:Value>s:Sender</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en\">Action not supported</s:Text></s:Reason></s:Fault>";
            }

            var media2Response = Media2Response(request);
            if (media2Response != null)
            {
                return media2Response;
            }

            var ptzResponse = PtzResponse(request);
            if (ptzResponse != null)
            {
                return ptzResponse;
            }

            var eventResponse = EventResponse(request);
            if (eventResponse != null)
            {
                return eventResponse;
            }

            if (request.Contains("GetSystemDateAndTime", StringComparison.Ordinal))
            {
                return SystemDateAndTime();
            }

            if (request.Contains("GetCapabilities", StringComparison.Ordinal))
            {
                return $"<tds:GetCapabilitiesResponse><tds:Capabilities><tt:Events><tt:XAddr>{EventServiceUri}</tt:XAddr><tt:WSSubscriptionPolicySupport>false</tt:WSSubscriptionPolicySupport><tt:WSPullPointSupport>true</tt:WSPullPointSupport><tt:WSPausableSubscriptionManagerInterfaceSupport>false</tt:WSPausableSubscriptionManagerInterfaceSupport></tt:Events>" +
                       $"<tt:Media><tt:XAddr>{MediaServiceUri}</tt:XAddr>" +
                       "<tt:StreamingCapabilities><tt:RTPMulticast>false</tt:RTPMulticast><tt:RTP_TCP>true</tt:RTP_TCP><tt:RTP_RTSP_TCP>true</tt:RTP_RTSP_TCP></tt:StreamingCapabilities>" +
                       "</tt:Media>" + (HasPtz ? $"<tt:PTZ><tt:XAddr>{PtzServiceUri}</tt:XAddr></tt:PTZ>" : string.Empty) +
                       "</tds:Capabilities></tds:GetCapabilitiesResponse>";
            }

            if (request.Contains("GetProfiles", StringComparison.Ordinal))
            {
                return "<trt:GetProfilesResponse>" +
                       Profile("Profile_1", "mainStream", 1920, 1080, 25, 4096) +
                       Profile("Profile_2", "subStream", 640, 360, 15, 512, ptz: true) +
                       "</trt:GetProfilesResponse>";
            }

            if (request.Contains("GetStreamUri", StringComparison.Ordinal))
            {
                return $"<trt:GetStreamUriResponse>{MediaUri(RtspUri.ToString())}</trt:GetStreamUriResponse>";
            }

            if (request.Contains("GetSnapshotUri", StringComparison.Ordinal))
            {
                return $"<trt:GetSnapshotUriResponse>{MediaUri(SnapshotUri.ToString())}</trt:GetSnapshotUriResponse>";
            }

            return "<s:Fault><s:Code><s:Value>s:Receiver</s:Value></s:Code><s:Reason><s:Text xml:lang=\"en\">Not implemented</s:Text></s:Reason></s:Fault>";
        }

        private static string Profile(string token, string name, int width, int height, int fps, int bitrate, bool ptz = false)
        {
            var ptzConfiguration = ptz ? "<tt:PTZConfiguration token=\"PTZ_1\"><tt:Name>PTZ</tt:Name><tt:UseCount>1</tt:UseCount><tt:NodeToken>Node_1</tt:NodeToken></tt:PTZConfiguration>" : string.Empty;
            return $"<trt:Profiles token=\"{token}\" fixed=\"true\"><tt:Name>{name}</tt:Name>" +
                   "<tt:VideoSourceConfiguration token=\"VSC_1\"><tt:Name>VSC</tt:Name><tt:UseCount>2</tt:UseCount><tt:SourceToken>VS_1</tt:SourceToken><tt:Bounds x=\"0\" y=\"0\" width=\"1920\" height=\"1080\"/></tt:VideoSourceConfiguration>" +
                   $"<tt:VideoEncoderConfiguration token=\"VEC_{token}\"><tt:Name>VEC</tt:Name><tt:UseCount>1</tt:UseCount><tt:Encoding>H264</tt:Encoding>" +
                   $"<tt:Resolution><tt:Width>{width}</tt:Width><tt:Height>{height}</tt:Height></tt:Resolution><tt:Quality>4</tt:Quality>" +
                   $"<tt:RateControl><tt:FrameRateLimit>{fps}</tt:FrameRateLimit><tt:EncodingInterval>1</tt:EncodingInterval><tt:BitrateLimit>{bitrate}</tt:BitrateLimit></tt:RateControl>" +
                   "<tt:H264><tt:GovLength>50</tt:GovLength><tt:H264Profile>Main</tt:H264Profile></tt:H264><tt:SessionTimeout>PT60S</tt:SessionTimeout></tt:VideoEncoderConfiguration>" +
                   ptzConfiguration + "</trt:Profiles>";
        }

        private static string MediaUri(string uri)
        {
            return $"<trt:MediaUri><tt:Uri>{uri}</tt:Uri><tt:InvalidAfterConnect>false</tt:InvalidAfterConnect><tt:InvalidAfterReboot>false</tt:InvalidAfterReboot><tt:Timeout>PT0S</tt:Timeout></trt:MediaUri>";
        }

        private static string Envelope(string body)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                   "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:tt=\"http://www.onvif.org/ver10/schema\" " +
                   "xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\">" +
                   $"<s:Body>{body}</s:Body></s:Envelope>";
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}
