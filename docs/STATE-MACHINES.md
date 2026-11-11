# State machines

Everything in Onvif.Open that has a lifecycle is a state machine written down in one place: a table of its states and
allowed moves (`StateMachine<T>` in C#, `defineMachine` in TypeScript, the same shape on both sides). Code changes a
state through the table, tests check the code against it, and the diagrams below are generated from it.

## Rules

- **One table per lifecycle**, next to the code that owns it. Do not set a status anywhere else: call `Move(from, to)`
  (`move` in TypeScript), which refuses a move that is not in the table and names the ones that are.
- **Hot paths do not throw**: the recording loop checks `CanMove` and logs a move outside the table instead.
- **Names are the API**: the states are the strings and enums the API returns; adding a state is a compatible change,
  renaming one is not.
- **Changing a table**: edit it, then run the tests with `UPDATE_STATE_DOCS=1` (`dotnet test` for the backend
  machines, `npm test` in `web/` for the web ones) to redraw the diagrams below. Without it, a stale diagram fails the
  tests (`StateMachinesTest`, `stateMachines.test.ts`).
- **A new machine**: declare it with the helper, add it to the list in that test, and add a section here with an empty
  `machine` block; the test fills it in.

## Machines

| Machine | Side | Table | Changed by | Seen in |
|---|---|---|---|---|
| [Camera recording](#camera-recording) | Backend | `Onvif.Open.Models/Recording/RecordingState.cs` (`RecordingStates.Machine`) | `CameraRecorder.SetState` (logs a move outside the table) | `GET /api/cameras` `state`; Home, Cameras, Live |
| [Plugin](#plugin) | Backend | `Onvif.Open.Models/Plugin/PluginState.cs` (`PluginStates.Machine`) | `PluginRunner` (moves through the table) | `GET /api/plugins` `state` |
| [Export](#export) | Backend | `Onvif.Open.Recorder/Exports/IExportStore.cs` (`ExportStatuses.Machine`) | `ExportService` (moves through the table) | `GET /api/exports` `status`; Exports page |
| [Case](#case) | Backend | `Onvif.Open.Recorder/Cases/CaseHolds.cs` (`CaseStatuses.Machine`) | `CaseService.UpdateAsync` | `GET /api/cases` `status`; Cases pages; open cases keep their video |
| [Alert](#alert) | Backend | `Onvif.Open.Recorder/Alerts/AlertRule.cs` (`AlertStates.Machine`, stored as `AcknowledgedAt`) | `AlertService.AcknowledgeAsync` | `GET /api/alerts` `status`; Alerts inbox and its count |
| [Gateway pairing](#gateway-pairing) | Backend | `Onvif.Open.Recorder/Gateways/GatewayPairing.cs` (`PairingStatus.Machine`) | `GatewayPairing` (in memory) | `GET /api/gateways/pairing/{id}` `state` |
| [Low-latency live stream](#low-latency-live-stream) | Backend | `Onvif.Open.Recording/Implementation/Mp4/Mp4LiveStream.cs` (`Mp4LiveStates.Machine`) | `Mp4LiveStream.Add`, `Close` | `GET /api/cameras/{id}/live.mp4` (503 while waiting, 415 when unsupported) |
| [Grid sub-stream](#grid-sub-stream) | Backend | `Onvif.Open.Recorder/Hosting/LiveViewService.cs` (`SubStreamStates.Machine`) | `LiveViewService` (its reader, and the idle sweep) | `live.mp4?stream=sub` |
| [Gateway tunnel](#gateway-tunnel) | Backend | `Onvif.Open.Recorder/Gateways/ControlPlaneClient.cs` (`TunnelStates.Machine`) | `ControlPlaneClient` (its tunnel loop) | gateways' status (online) at the control plane |
| [Cloud gateway](#cloud-gateway) | Backend | `Onvif.Open.Recorder/Gateways/CloudGateways.cs` (`CloudGatewayStates.Machine`, stored as `HostState`) | `CloudGateways` (create, start again, heartbeat, start timeout) | `GET /api/gateways` `hosting.state`; Settings › Gateways |
| [Two-factor sign-in](#two-factor-sign-in) | Backend | `Onvif.Open.Recorder/Security/TwoFactor.cs` (`TwoFactorStates.Machine`, stored as `TwoFactorState`) | `TwoFactorService` (setup, enable, disable, reset) | `GET /api/auth/me` and `GET /api/users` `twoFactor`; Account, Users |
| [Subscription](#subscription) | Backend | `Onvif.Open.Recorder/Billing/BillingService.cs` (`SubscriptionStates.Machine`, stored as `SubscriptionState`) | `BillingService` (trial start and end, the payment provider's webhooks) | `GET /api/billing` `state`; Settings › Billing |
| [Session](#session) | Web | `web/src/features/auth/session.ts` (`sessionMachine`, `sessionOf`) | `AuthContext` holds it; `RequireAuth` routes on it | sign-in, password change, sign-out |
| [Sign-in steps](#sign-in-steps) | Web | `web/src/features/auth/signInSteps.ts` (`signInSteps`) | `LoginPage` (moves through the table) | Sign in: password, then the app's code; or single sign-on |
| [Two-factor setup](#two-factor-setup) | Web | `web/src/features/auth/twoFactorSetup.ts` (`twoFactorSetupSteps`) | `TwoFactorSetup` (moves through the table) | Account › Two-factor sign-in; the required setup page |
| [Add-camera wizard](#add-camera-wizard) | Web | `web/src/features/cameras/wizard/steps.ts` (`wizardSteps`) | `AddCamerasPage` (moves through the table) | Cameras › Add cameras |
| [Camera in the add-camera wizard](#camera-in-the-add-camera-wizard) | Web | `web/src/features/cameras/wizard/candidates.ts` (`candidateMachine`) | `changeCandidate`, `afterSharedChange`, `startTest` | each camera's state in the wizard |
| [PTZ hold to move](#ptz-hold-to-move) | Web | `web/src/features/live/ptz/ptzHold.ts` (`ptzHoldMachine`) | `createPtzHold` (`press`, `release`) | the PTZ panel of an enlarged live tile |
| [Clip marks](#clip-marks) | Web | `web/src/features/investigate/clipMarks.ts` (`markMachine`) | `markIn`, `markOut`, `clearMarks` (move through the table) | Investigate and cases: Mark in / Mark out / Clear |

### Camera recording

Reported to plugins and the API on every change (`CameraStateChange`); Reconnecting starts an `offline` event, Recording ends it (Investigate, alert rules).

<!-- machine: Camera recording -->
```mermaid
stateDiagram-v2
    [*] --> Stopped
    Stopped --> Connecting : started
    Connecting --> Recording : first frame
    Connecting --> Reconnecting : connection failed
    Connecting --> Stopped : stopped
    Recording --> Reconnecting : stream lost or stalled
    Recording --> Stopped : stopped
    Reconnecting --> Connecting : retry after a delay
    Reconnecting --> Stopped : stopped
```
<!-- /machine -->

### Plugin

A faulted plugin receives nothing until the recorder restarts.

<!-- machine: Plugin -->
```mermaid
stateDiagram-v2
    [*] --> Created
    Created --> Running : StartAsync succeeded
    Created --> Faulted : StartAsync threw
    Running --> Stopped : host stopping
    Faulted --> [*]
    Stopped --> [*]
```
<!-- /machine -->

### Export

An export interrupted by a restart runs again (Running → Running). Failed and Expired are final: export again instead.

<!-- machine: Export -->
```mermaid
stateDiagram-v2
    [*] --> Queued
    Queued --> Running : its turn in the queue
    Running --> Running : resumed after a restart
    Running --> Ready : MP4 written and hashed
    Running --> Failed : no video, or ffmpeg failed
    Ready --> Expired : 7 days passed: file deleted
    Failed --> [*]
    Expired --> [*]
```
<!-- /machine -->

### Case

While a case is open, retention keeps the video of its clips.

<!-- machine: Case -->
```mermaid
stateDiagram-v2
    [*] --> Open
    Open --> Closed : closed by an operator
    Closed --> Open : reopened by an operator
```
<!-- /machine -->

### Alert

Acknowledging an acknowledged alert keeps the first acknowledgement.

<!-- machine: Alert -->
```mermaid
stateDiagram-v2
    [*] --> Open
    Open --> Acknowledged : an operator saw to it
    Acknowledged --> [*]
```
<!-- /machine -->

### Gateway pairing

The gateway side (`ControlPlaneClient`) asks for a new code when one expires.

<!-- machine: Gateway pairing -->
```mermaid
stateDiagram-v2
    [*] --> Waiting
    Waiting --> Paired : an administrator entered the code
    Waiting --> Expired : 10 minutes passed
    Paired --> [*]
    Expired --> [*]
```
<!-- /machine -->

### Low-latency live stream

A new viewer joins at the last key frame; every drop back to waiting drops the viewers, whose players reconnect.

<!-- machine: Low-latency live stream -->
```mermaid
stateDiagram-v2
    [*] --> WaitingForKeyFrame
    WaitingForKeyFrame --> Streaming : H.264 key frame with its parameter sets
    Streaming --> WaitingForKeyFrame : camera reconnected, new parameter sets, or closed: viewers dropped
    WaitingForKeyFrame --> Unsupported : not H.264: players use HLS
    Streaming --> Unsupported : not H.264: players use HLS
    Unsupported --> Streaming : H.264 key frame (the camera changed codec)
```
<!-- /machine -->

### Grid sub-stream

A connection to the camera, so it is only open while a grid tile watches it.

<!-- machine: Grid sub-stream -->
```mermaid
stateDiagram-v2
    [*] --> Closed
    Closed --> Connecting : a grid tile asks for it
    Connecting --> Streaming : the camera sends frames
    Connecting --> Retrying : could not connect
    Streaming --> Retrying : the stream dropped
    Retrying --> Connecting : after 1 s, doubling up to 30 s
    Connecting --> Closed : nobody watches, the camera changed or is gone
    Streaming --> Closed : nobody watches, the camera changed or is gone
    Retrying --> Closed : nobody watches, the camera changed or is gone
```
<!-- /machine -->

### Gateway tunnel

The only connection a gateway makes: outbound, so nothing at the site listens on the internet. While it is down the gateway keeps recording.

<!-- machine: Gateway tunnel -->
```mermaid
stateDiagram-v2
    [*] --> Unpaired
    Unpaired --> Connecting : paired: the gateway has its token
    Connecting --> Open : the control plane accepted the WebSocket
    Connecting --> Retrying : could not connect
    Open --> Retrying : the tunnel closed
    Retrying --> Connecting : after 1 s, doubling up to 30 s
    Retrying --> Unpaired : removed at the control plane: pairing again
```
<!-- /machine -->

### Cloud gateway

A gateway the cloud runs for a customer whose cameras are on the internet. Starting it again gives it a new token; removing it stops it.

<!-- machine: Cloud gateway -->
```mermaid
stateDiagram-v2
    [*] --> Starting
    Starting --> Running : it connected
    Starting --> Failed : it could not be started, or did not connect in time
    Failed --> Running : it connected after all
    Failed --> Starting : an administrator started it again
    Running --> Starting : an administrator started it again
```
<!-- /machine -->

### Two-factor sign-in

A user's authenticator app. Signing in asks for its code (or a recovery code) once it is On; an organization can require it.

<!-- machine: Two-factor sign-in -->
```mermaid
stateDiagram-v2
    [*] --> Off
    Off --> Pending : set up: the app got its secret
    Pending --> On : a code from the app was entered
    Pending --> Off : an administrator reset it
    On --> Off : turned off, or reset by an administrator
```
<!-- /machine -->

### Subscription

An organization's plan where billing is on: it tries the trial plan, then pays at the payment provider, whose webhooks move it (they can come twice or out of order: a move the table does not have is skipped). Recording never stops for billing; an ended subscription adds no cameras.

<!-- machine: Subscription -->
```mermaid
stateDiagram-v2
    [*] --> Trialing
    Trialing --> Active : paid for a plan
    Trialing --> Canceled : the trial ended unpaid
    Active --> PastDue : a payment failed
    PastDue --> Active : paid
    PastDue --> Canceled : unpaid for too long, or canceled
    Active --> Canceled : canceled
    Canceled --> Active : subscribed again
```
<!-- /machine -->

### Session

A refresh token the recorder rejects signs out from any state (`client.onSignedOut`).

<!-- machine: Session -->
```mermaid
stateDiagram-v2
    [*] --> resuming
    resuming --> signedIn : stored session still valid
    resuming --> mustChangePassword : stored session, temporary password
    resuming --> mustSetUpTwoFactor : stored session; the organization requires two-factor
    resuming --> signedOut : no or expired session
    signedOut --> signedIn : signed in, or set up the organization
    signedOut --> mustChangePassword : signed in with a temporary password
    signedOut --> mustSetUpTwoFactor : signed in; the organization requires two-factor
    mustChangePassword --> signedIn : password changed
    mustChangePassword --> mustSetUpTwoFactor : password changed; two-factor still to set up
    mustChangePassword --> signedOut : Sign out, or session rejected
    mustSetUpTwoFactor --> signedIn : two-factor sign-in turned on
    mustSetUpTwoFactor --> signedOut : Sign out, or session rejected
    signedIn --> mustSetUpTwoFactor : the organization started requiring two-factor
    signedIn --> signedOut : Sign out, or session rejected
```
<!-- /machine -->

### Sign-in steps

With two-factor sign-in on, the password gets a five-minute challenge, sent back with the app's code (or a recovery code). Single sign-on asks for the work email and leaves for the identity provider, which sends people back to `/sso`.

<!-- machine: Sign-in steps -->
```mermaid
stateDiagram-v2
    [*] --> password
    password --> code : the password was right; two-factor sign-in is on
    code --> password : Start over, or the challenge expired
    password --> singleSignOn : Use single sign-on, or back from the provider with a problem
    singleSignOn --> password : Use a password
```
<!-- /machine -->

### Two-factor setup

The page's side of a user's Two-factor sign-in (Off, Pending, On on the recorder): the recovery codes are shown once, at the end.

<!-- machine: Two-factor setup -->
```mermaid
stateDiagram-v2
    [*] --> start
    start --> scan : Set up: the recorder made a secret
    scan --> start : Cancel
    scan --> codes : a code from the app was right: two-factor is on
    codes --> [*]
```
<!-- /machine -->

### Add-camera wizard

Done is final: the cameras were added.

<!-- machine: Add-camera wizard -->
```mermaid
stateDiagram-v2
    [*] --> find
    find --> connect : cameras chosen
    connect --> find : Back
    connect --> name : at least one camera ready
    name --> connect : Back
    name --> done : Add cameras
    done --> [*]
```
<!-- /machine -->

### Camera in the add-camera wizard

A camera whose sign-in changes after a test is tested again, so it is never added with a password nobody tested. Sign-in fields are locked while a test runs.

<!-- machine: Camera in the add-camera wizard -->
```mermaid
stateDiagram-v2
    [*] --> untested
    untested --> testing : Test cameras
    testing --> ok : signed in, picture taken
    testing --> unauthorized : password refused
    testing --> unreachable : no answer
    ok --> testing : Test again
    unauthorized --> testing : Test again
    unreachable --> testing : Test again
    ok --> untested : its sign-in changed
    unauthorized --> untested : its sign-in changed
    unreachable --> untested : its sign-in changed
```
<!-- /machine -->

### Clip marks

A mark on the wrong side of the other one replaces it, so the marks are always in order.

<!-- machine: Clip marks -->
```mermaid
stateDiagram-v2
    [*] --> none
    none --> in : Mark in
    none --> out : Mark out
    in --> in : Mark in again
    in --> clip : Mark out after the in point
    in --> out : Mark out before the in point
    out --> out : Mark out again
    out --> clip : Mark in before the out point
    out --> in : Mark in after the out point
    clip --> clip : move a mark, still in order
    clip --> in : Mark in after the out point
    clip --> out : Mark out before the in point
    in --> none : Clear
    out --> none : Clear
    clip --> none : Clear
```
<!-- /machine -->

### PTZ hold to move

Every move stops by itself after 1.5 s and is renewed each second while held, so a browser that goes away mid-move cannot leave the camera turning.

<!-- machine: PTZ hold to move -->
```mermaid
stateDiagram-v2
    [*] --> idle
    idle --> moving : a direction is pressed
    moving --> moving : still held: renewed every second, or another direction
    moving --> idle : released, or the panel closes: stop sent
```
<!-- /machine -->
