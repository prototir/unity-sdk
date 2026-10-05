# Prototir SDK for Unity

The official Unity integration for [Prototir](https://prototir.com), where creators publish
playable prototypes and testers play them and leave feedback. The package connects Web and native
(Windows, macOS, Linux) builds to lifecycle signals, analytics events, scores and sessions, and gives
testers **Feedback & tools**: Screenshot, Comment, Console and Performance, with nothing for you to
write. Web builds also get persistent storage and managed text generation. Native builds pair with
a tester's account and report over HTTP. Project Setup and export checks catch unsupported settings
before upload, and **Publish to Prototir** uploads straight from the editor.

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

A tag keeps the project on one version until you choose to move. The editor checks for a newer
release once a day: see [Staying up to date](#staying-up-to-date).

After installation:

1. Open **Prototir > Project Setup** and choose what you are building: **Web** or **Native**.
2. Select **Fix all available**, then resolve any remaining blocking items for that target.
3. Import **Basic Integration** from the package's Samples tab if you want a small API example.
4. Export a browser or native release using the appropriate **Export for Prototir** command.
   Native builds do not need `index.html`; see [Native builds](#native-builds).

## Basic use

Browser builds use the host connection. Native releases must pair before sending sessions or
feedback; see [Native builds](#native-builds). Storage and AI in this example
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

## Feedback & tools

Testers get one **Feedback & tools** control, with its tools unfolding inside the same border:

- **Screenshot** captures the frame; the tester places a pin and writes what they mean.
- **Comment** is a plain comment on the prototype.
- **Console** is recorded from start. Testers can copy it or attach it to a comment, where it shows
  collapsed.
- **Performance** charts frame rate, slowest frame and memory, recorded only while open. A summary
  can be copied or attached.

A screenshot, log or summary is always sent with a message, never on its own. Comments follow the
prototype's moderation and comment settings.

### In Web builds

On Prototir the player draws the control and feedback is on with no code. Prototir also replaces
the runtime the export bundles with the current one, so testers get new tools without the build
being exported again.

Add a `PrototirReview` component to a scene to capture Unity's own frame for screenshots, pause the
game while the tester writes, or connect a build you host yourself:

| Field | Meaning |
| --- | --- |
| `ProjectId` | Stable identifier for this prototype's feedback. |
| `BuildId` | Recorded with the feedback so you know which build a screenshot came from. |
| `Corner` | `bottom-left` (default), `bottom-right`, `top-left`, or `top-right`. |
| `Launcher` | `auto` (default) lets Prototir draw the control on its own surfaces; `host` draws nothing so your own UI opens it. |
| `Theme` | `auto` (default) follows the player's light/dark preference; `light` or `dark` pins it. |
| `ApiBase` | Prototir API base URL. With `PrototypeSlug`, lets a build hosted outside Prototir post feedback. |
| `PrototypeSlug` | The prototype these comments belong to, as it appears in its Prototir URL. |
| `PauseWhileReviewing` | Sets `Time.timeScale` to zero while a panel is open. |
| `ReviewVisibilityChanged` | Fires with `true`/`false` so you can pause audio or your own input. |

Screenshots are taken with `ScreenCapture.CaptureScreenshotAsTexture` after `WaitForEndOfFrame`, so
they match what the player saw. While a panel is open the component clears
`WebGLInput.captureAllKeyboardInput`, otherwise the game would swallow the tester's typing. A build
hosted elsewhere without `ApiBase` and `PrototypeSlug` shows no feedback control, because comments
would have nowhere to go.

### In native builds

The same control appears in the bottom-left corner of any native build that knows its prototype,
which it does once uploaded to Prototir. The first post asks the tester to approve the build at
prototir.com/link; comments then post as that account. Testers can disconnect a build from their
Prototir account settings.

```csharp
PrototirSdk.FeedbackTools = false;          // hide it (or untick Feedback Tools in PrototirSettings)
PrototirSdk.ShowFeedbackScreen();           // the same comment screen, from your own button
string log = PrototirSdk.ConsoleText();     // what the Console tool has recorded
await PrototirSdk.SendFeedbackAsync("...");  // post a comment from code
```

The console records Unity's log, warnings and errors (with the first lines of their stack) from
start; the performance sampler runs only while its panel is open. The comment screen keeps an
unfinished comment for the run, opens pairing when needed, and reuses its submission id when a post
is retried. These are flat desktop overlays, not headset UI: VR projects should hide them and use
their own interface with the pairing events and `SendFeedbackAsync`.

## Staying up to date

The Package Manager does not offer updates for a package installed from a Git URL, so the SDK checks
itself. Once a day (or with **Prototir > Check for SDK Updates**) the editor looks at this
repository's release tags. When a newer one exists, **Prototir > Project Setup** shows it with
**What's new** and **Update**, which moves the project to the new tag; nothing changes until you
press it. Project Setup also flags a Web template copy in `Assets/WebGLTemplates` that is older than
the installed SDK.

Web builds on Prototir always run the current feedback runtime. A native build keeps the SDK it was
built with, so build again after updating to give its testers new tools.

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

`PrototirSdk.SendFeedbackAsync("...")` posts a comment as the tester who approved the build, and
[Feedback & tools](#in-native-builds) gives testers the same without code. No session is needed
first, because approving the pairing is the stronger signal.

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
