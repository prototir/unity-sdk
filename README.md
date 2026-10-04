# Prototir SDK for Unity

The official Unity integration for prototypes hosted on [Prototir](https://prototir.com). The
package connects browser and native builds to lifecycle signals, analytics events, scores, and
feedback. Browser builds also use persistent SDK storage, managed text generation, and screenshot
feedback. Native builds pair with a tester's account and report over HTTP. Export checks help catch
unsupported browser settings before upload.

## Compatibility

- Unity 6 (`6000.0`) or newer
- Browser: Web target with the single-threaded standard runtime profile
- Native: Windows, macOS, and Linux release players, with device pairing

Older Unity releases are not supported. The package declares its minimum editor version, and the
Project Setup window reports an incompatible editor as a blocking issue.

## Install

In Unity Package Manager, choose **Add package from git URL** and use a tagged release:

```text
https://github.com/prototir/unity-sdk.git#v0.4.0
```

Pin a tag in production so an SDK update cannot change an existing project unexpectedly.

After installation:

1. Open **Prototir > Project Setup** and choose what you are building: **Web** or **Native**.
2. Select **Fix all available**, then resolve any remaining blocking items for that target.
3. Import **Basic Integration** from the package's Samples tab if you want a small API example.
4. Export a browser or native release using the appropriate **Export for Prototir** command.
   Native builds do not need `index.html`; see [Native builds](#native-builds).

## Basic use

Browser builds use the host connection. Native releases must pair before sending sessions or
text feedback; see [Native builds](#native-builds). Storage and AI in this example
are browser features; native calls use local mocks.

```csharp
using Prototir;

PrototirSdk.Ready();
PrototirSdk.Event("level_complete", new LevelEvent { level = 2 });
PrototirSdk.Score(1200);

await PrototirSdk.StorageSetAsync("difficulty", "hard");
var difficulty = await PrototirSdk.StorageGetAsync("difficulty");

var quest = await PrototirSdk.AiGenerateAsync(new PrototirAiOptions
{
    Prompt = "Give the player a short quest hook.",
    MaxTokens = 80
});
```

Call `Ready()` after the first genuinely interactive frame. Event names are normalized to
lowercase and accept letters, numbers, `_`, `.`, `:`, and `-`. Keep payloads small and free of
personal data.

## Project Setup and export checks

The window starts with **Building for: Web | Native**. The choice is saved per project (in
`UserSettings/`), and until you pick it follows the active build target.

- **Every build:** Unity version, enabled scenes, development and profiling flags, product name.
- **Web:** the installed Web module, threads, Run In Background, debug symbols, PWA output, player
  template, and data caching. Blocking issues stop Web builds.
- **Native:** build support for this machine's desktop platform, and a desktop active build target.
  None of the Web rules apply, and the window never suggests switching to Web.

Recommendations remain visible without silently changing the project.

**Fix all available** changes only settings with a single safe value. It also installs the included
edge-to-edge Web template, which removes Unity's default page frame and duplicate fullscreen UI.
Every successful Web build receives a root `prototir.json`; create a customized source manifest
through **Prototir > Create or Open Project Manifest** when the generated defaults are not enough.

Run the structural validator outside Unity with:

```bash
node tools/export-validator.mjs /path/to/export
```

## Editor behavior

The Editor exposes mock readiness/events/scores and can test device pairing, but never reports
development play as a real session. Native release players report Ready, events, scores, sessions,
and text feedback after pairing. Outside Web players, SDK storage remains in memory and managed AI
requires an explicit local `PrototirSdk.MockAiHandler`; neither is connected to a hosted native
storage or AI service. Use your own persistent save system for native builds.

## Screenshot feedback

Add a `PrototirReview` component to a scene to give testers a floating feedback button in Web
builds. They capture the current view, drop a pin on that screenshot and write a comment.

| Field | Meaning |
| --- | --- |
| `ProjectId` | Stable identifier. Reviews exported from another project are refused on import. |
| `BuildId` | Recorded with the feedback so you know which build a screenshot came from. |
| `Corner` | `bottom-left` (default), `bottom-right`, `top-left`, or `top-right`. |
| `Launcher` | `auto` (default) lets Prototir draw the control on its own surfaces; `watermark` always shows the Prototir mark; `host` draws nothing. |
| `Theme` | `auto` (default) follows the player's light/dark preference; `light` or `dark` pins it. |
| `ApiBase` | Prototir API base URL. With `PrototypeSlug`, lets a build hosted outside Prototir post feedback. |
| `PrototypeSlug` | The prototype these comments belong to, as it appears in its Prototir URL. |
| `PauseWhileReviewing` | Sets `Time.timeScale` to zero while the panel is open. |
| `ReviewVisibilityChanged` | Fires with `true`/`false` so you can pause audio or your own input. |

Inside the Prototir player, Prototir draws **Feedback** in its own control bar and the component
stays out of the way. Anywhere else it shows the Prototir mark, which opens an icon menu with
**Screenshot** and **Review files** while offline, or **Screenshot** and **Comments** when
connected to Prototir. **Open on Prototir** appears when configured. Choosing Screenshot
captures the frame and opens the focused composer; Review files contains import and export.

Screenshots are taken with `ScreenCapture.CaptureScreenshotAsTexture` after `WaitForEndOfFrame`, so
they match what the player saw. While the panel is open the component clears
`WebGLInput.captureAllKeyboardInput`, otherwise the game would swallow the tester's typing.

The component is inert outside Web builds and in the Editor. There is nothing to remove for a native
build, but testers will not see the button there.

For browser builds hosted elsewhere, `PrototirReview.ApiBase` and `PrototypeSlug` configure the
browser panel's connection. Without that connection, the browser panel saves review files offline.

On Prototir the capture opens one host composer, where the tester places a pin and explicitly posts an ordinary comment. In a Web build you host yourself the panel saves a
`feedback.prototir-review.json` file that the tester sends you and you reload with **Import review**.
See the [Web SDK README](https://github.com/prototir/web-sdk#screenshot-feedback) for the file format
and its limits.

### Text feedback from a native build

A native build pairs through a code approved at `prototir.com/link`, receiving authorization for
one prototype. After approval, call `PrototirSdk.SendFeedbackAsync(text)` from your own comment UI.
The browser `PrototirReview` screenshot overlay does not run in a native player.

The SDK reuses the pairing until it is revoked or expires. Keep the tester's draft on send failure
and offer a retry. Testers can disconnect a build from their Prototir account settings.

## Native builds

A Web build takes everything from the page around it: the visitor is already signed in, and the
shell watches the prototype and reports for it. A native build has none of that, so the SDK does it
itself. None of this code reaches a Web build, which strips it at compile time.

You do not configure the slug. Prototir writes it into the .zip as you upload the build, into a
`prototir-prototype.json` beside the executable, and the SDK reads it from there. The slug does not
exist until the prototype does, so there was never a value you could have put in your first export.

Override it with **Prototir > Create Settings** when you need to: a build you ship outside
Prototir, or an installer Prototir cannot write into. A game that decides its prototype at runtime
can call `PrototirSdk.Configure("your-slug")`. The injected slug wins over the settings asset,
because it travelled with that exact download.

Since `v0.2.1` the SDK includes a ready-to-use pairing screen over your game:

```csharp
PrototirSdk.ShowPairingScreen();
```

It shows the code and a scannable QR, opens the approval link, and handles approval, failure,
disconnecting and an already connected build. The screen uses Prototir's dark colors and requires
no prefab or scene setup. It is available in the Editor and native builds. Import the **Native Pairing** sample to see
how to build your own UI from the supported pairing events below.

```csharp
void Start()
{
    PrototirSdk.Ready();
    PrototirSdk.PairingStarted += request => codeLabel.text = request.Code;
    if (!PrototirSdk.IsPaired) _ = PrototirSdk.BeginPairingAsync();
}
```

The SDK only draws when you call `ShowPairingScreen()`. For your own UI, it hands you the code, the
verification link and a ready-made QR (`request.QrSvg`).

Give the tester a way to act on it. Text they cannot select is a dead end, so offer
`Application.OpenURL(request.VerificationUrl)` on desktop, and the QR or the bare code where a
browser on this machine helps nobody, such as a headset. The sample does both. `PairingSucceeded` and `PairingFailed` cover the rest; `PairingFailed`
also fires when a paired build is refused later, which means the tester revoked it.

`Ready`, `Event` and `Score` accumulate one session rather than one request each, and the SDK
reports it for you every 30 seconds while the game runs, and again when the window loses focus.
You do not have to call anything. `PrototirSdk.FlushSessionAsync()` is there for a natural break,
such as the end of a run, if you want the numbers to land sooner.

Repeating costs nothing: the first report returns an id the rest carry, so the server updates one
row rather than counting a play per report. On quit there is no time left to send, so whatever the
last report missed is written under `Application.persistentDataPath` and goes out at the next
launch. That covers the tester playing on a plane as well.

`PrototirSdk.SendFeedbackAsync("...")` posts a comment as the tester who approved the build. No
session is needed first, because approving the pairing is the stronger signal.

Pairing works in the Editor, so you can build the screen without exporting every time. Reporting
does not: pressing Play is not a play, and counting it would put your own testing in your own
numbers.

## Publish to Prototir

**Prototir > Publish to Prototir (Web)** and **(Native)** build, upload, and open your browser on
the upload page with the build already attached. You finish the details there and publish; the
editor never publishes on its own.

- **First time on a computer:** the editor asks to be linked to your account. Your browser opens
  an approval page with a code; approve it once and every project on this machine can publish.
  The link can only upload builds. See or remove it under **Linked editors** on your account page,
  or use **Prototir > Unlink This Editor**.
- **Project with a slug** (Prototir > Create Settings): the browser opens that prototype's Studio
  page instead, to replace its web build or add this native build.
- **Uploads wait for you** for 7 days, listed with their upload time on the upload page (new
  prototype) or in Studio (project with a slug), so closing or reloading the browser loses
  nothing. Use or discard each one there. A newer upload for the same prototype and platform
  replaces the one still waiting.
- **Native builds** are labelled with Unity's default architecture: x86-64 for Windows and Linux,
  universal for macOS. For anything else, use Export and pick the architecture on the upload page.
- Publish needs the editor UI; batch builds keep using Export.

## Export buttons

Two entries under the **Prototir** menu produce a ZIP without uploading it:

- **Export for Prototir (Web)** runs the Project Setup checks, builds, and zips the result.
- **Export for Prototir (Native)** builds for the active desktop target. It does not switch
  platform for you, because switching reimports the whole project.

Both produce a ZIP ready to drop on the upload page, and both write `prototir-build.json` beside the
build. Prototir records that id from the archive, and a running build reports the same id when it
pairs; a match shows the build running is the build that was uploaded, and nothing more. Both sides
come from a file you control, so it is not verification, security or anti-cheat. It catches an old
build being run against a new upload.

## Documentation and examples

- [Package documentation](Documentation~/index.md)
- [Unity examples](https://github.com/prototir/unity-examples)
- [Creator documentation](https://prototir.com/docs/creators?runtime=unity#setup)
- [Release process](DISTRIBUTION.md)

## Validate the package

```bash
node tools/test.mjs
dotnet test Tests~/Prototir.Native.Tests
```

The Node check validates package structure, bridge behavior, the project assistant, export
validator, and Web template. The dotnet tests cover the native path: pairing, the session
recorder and the session queue run against fake HTTP, a fake clock and a fake delay, so a poll loop
that waits ten minutes for a deadline finishes instantly and nothing touches the network. A tagged release must additionally compile in Unity 6 and pass a real
Prototir sandbox play test.

## License

[MIT](LICENSE.md)

### Simple native feedback

Call `PrototirSdk.ShowFeedbackScreen()` from your game's Feedback button. The desktop screen keeps an unfinished comment during this run, opens browser pairing when needed, and asks the tester to press Post after pairing. Failed requests keep the draft and reuse its submission ID. Comments pass the API's existing text checks.

For custom pause handling, `Prototir.Native.PrototirFeedbackScreen.Show()` returns the screen and exposes `Closed`. These flat desktop overlays are not headset UI; VR projects should use their own interface and the pairing events / `SendFeedbackAsync`. Native screenshots are not provided.
