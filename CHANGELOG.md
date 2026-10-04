# Changelog

## 0.4.0 - 2026-10-04

- **Feedback & tools** for testers, with nothing to set up: Screenshot, Comment, Console and
  Performance. Web builds get the web SDK's control. Native builds get the same control in the
  bottom-left corner whenever the build knows its prototype: the console is recorded from start,
  the performance chart records only while open, and a screenshot or log is always sent with a
  message. Turn it off with **Feedback Tools** in the settings asset or
  `PrototirSdk.FeedbackTools = false`; `PrototirSdk.ConsoleText()` returns the recorded console.
- The package now depends on Unity's Image Conversion module, used to encode native screenshots.
- The bundled review runtime (web builds) now draws the feedback control like the Prototir badge
  and uses the website's current palette.

## 0.3.0 - 2026-09-29

- Added **Prototir > Publish to Prototir (Web)** and **(Native)**: build, upload straight to
  Prototir, and open the upload page (or the prototype's Studio page when the project has a slug)
  with the build attached, ready to finish and publish. The editor links to your account once per
  computer through a browser approval; **Prototir > Unlink This Editor** removes the link.
  Uploads wait on the website for 7 days, one per prototype and platform, until you use or
  discard them.
- **Project Setup** now asks what you are building, **Web** or **Native**, and shows only that
  target's checks. It no longer recommends switching to Web, and a missing Web module no longer
  blocks a native project.
- "Download" is now "Native" wherever it names the kind of build: **Export for Prototir (Native)**
  (was **(Download)**), the **Native Pairing** sample (was **Download Pairing**, now in
  `Samples~/NativePairing`), and the docs. Re-import the sample from the Package Manager if you
  used the old one.
- The export dialog now names the current upload page steps (**Add a build > Windows**).
- Web builds no longer fail to compile with "The name 'WebGLInput' does not exist" or "'ScreenCapture'
  does not exist" in projects that trim Unity's built-in modules (found building the example
  project with Unity 6000.3). The package now depends on the Screen Capture module it uses, and
  the review overlay reaches the WebGL keyboard switch at runtime, leaving the keyboard as it is
  when the module is absent.

## 0.2.1 - 2026-09-28

- Added `PrototirSdk.ShowFeedbackScreen()` for desktop text feedback: one comment, browser pairing when needed, draft retention during this run, and one submission ID across retries. No native screenshot capture is implied.
- Included the built-in pairing screen in an immutable installable release. The native screens were compiled and exercised in a Unity 6 Windows player.
- Browser captures now open the host composer through Web SDK 0.2.8.

- Added `PrototirSdk.ShowPairingScreen()` for downloadable builds: a built-in overlay with the
  code, scannable QR, approval state, success, failure, and disconnect action. Custom UI through
  the pairing events remains supported.
- Native HTTP requests now return to Unity's main thread before creating `UnityWebRequest`.
  Pairing polling and session reports can resume on a worker after protocol awaits.
- Pairing token and pending-session paths are captured on Unity's main thread. Approval can
  save the token after polling resumes on a worker without calling `persistentDataPath` there.
- A downloadable build now tells Prototir which build it is at launch, so a download-only
  prototype switches on the first time anyone runs it. This used to require pairing, which
  asked a creator to link a build to their account before their own download counted for
  anything, and made every prototype a separate chore. Pairing is about identity; this is
  about which build is running, and they are now separate.
- A build that is not the uploaded one, or that carries no build id because it was zipped by
  hand, says so in the log rather than leaving a creator with a prototype that does nothing.

## 0.2.0 - 2026-09-21

- Added a native transport for downloadable builds: device-code pairing, a stored per-prototype
  token, one accumulated session per play, and comments posted as the tester who approved the
  build. A Web build strips all of it at compile time.
- Added `PrototirSdk.IsPaired`, `BeginPairingAsync`, `CancelPairing`, `Unpair`, `SendFeedbackAsync`
  and `FlushSessionAsync`, plus the `PairingStarted`, `PairingSucceeded` and `PairingFailed` events.
- A play in progress now reports itself every 30 seconds, and when the window loses focus.
  The only moment a session was ever sent was the next launch, so a tester who played once
  and never opened the build again reported nothing at all, which is the most common way a
  prototype gets tried.
- A session is serialized to the exact contract: the server models its id and score as
  optional, and an always-present `"sessionId": ""` could not be parsed. The endpoint
  answered 500, the queue treats 5xx as retry-later, and so every session ever recorded
  piled up on disk and none were sent, silently. An always-present `"score": 0` would also
  have put a nought on a leaderboard for every play that never scored.
- `Configure` now drains the session queue. The drain ran only at boot, before a runtime
  `Configure` could say where to send, so a build that learned its prototype at runtime kept
  every session it ever recorded and sent none of them.
- Added an on-disk session queue: a play is written down on quit and sent at the next launch, so
  closing the game or being offline no longer loses it. This also removes a hang on quit, where
  waiting for a `UnityWebRequest` blocked the very player loop that had to complete it.
- Added `PrototirSdk.Configure(slug, apiBaseUrl, deviceLabel)` for a build that learns its
  prototype at runtime, and `PrototirSdk.PairingState` for drawing the difference between "not
  paired" and "waiting for approval". The Godot addon already had both.
- `PairingState` is derived from the stored token rather than remembered. A static field does not
  survive the process that set it, so a build paired yesterday started today reporting
  `NotPaired` while it held a valid token, and a game drawing from it would have shown a pairing
  prompt to someone already paired. The rule now lives beside the protocol and is tested.
- Added a **Download Pairing** sample: a working pairing screen in one file, with no scene
  setup, including opening the approval page and copying the code. A game window has no
  selectable text, so a printed URL on its own leaves the tester retyping it off a screen.
- The default API endpoint is now `https://api.prototir.com/api`. It was `prototir.com/api`,
  which serves no `/api` path, so a downloadable build could not reach Prototir at all; the
  whole native path was dead in production until a real build was run against it.
- Added **Prototir > Create Settings**, and `PrototirSettings` for the prototype slug, API base URL
  and device label.
- Added **Prototir > Export for Prototir (Web)** and **(Download)**, which build, zip and write
  `prototir-build.json`.

## 0.1.0 - 2026-08-26

- Initial public Unity 6 Web integration for Prototir protocol version 1.
- Added readiness, analytics events, scores, persistent storage, and managed text generation.
- Added Editor and non-Web mocks with correlated asynchronous responses.
- Added Project Setup checks, safe fixes, build-time validation, and manifest generation.
- Added an edge-to-edge Web template and structural export validator.
