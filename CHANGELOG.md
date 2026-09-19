# Changelog

## Unreleased

- Added a native transport for downloadable builds: device-code pairing, a stored per-prototype
  token, one accumulated session per play, and comments posted as the tester who approved the
  build. A Web build strips all of it at compile time.
- Added `PrototirSdk.IsPaired`, `BeginPairingAsync`, `CancelPairing`, `Unpair`, `SendFeedbackAsync`
  and `FlushSessionAsync`, plus the `PairingStarted`, `PairingSucceeded` and `PairingFailed` events.
- Added an on-disk session queue: a play is written down on quit and sent at the next launch, so
  closing the game or being offline no longer loses it. This also removes a hang on quit, where
  waiting for a `UnityWebRequest` blocked the very player loop that had to complete it.
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
