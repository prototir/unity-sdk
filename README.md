# Prototir SDK for Unity

The official Unity integration for prototypes hosted on [Prototir](https://prototir.com). The
package connects a Unity Web build to Prototir lifecycle signals, analytics events, scores,
persistent storage, and managed text generation. It also validates project and export settings so
unsupported builds fail early with actionable guidance.

## Compatibility

- Unity 6 (`6000.0`) or newer
- Web build target
- Single-threaded standard runtime profile

Older Unity releases are not supported. The package declares its minimum editor version, and the
Project Setup window reports an incompatible editor as a blocking issue.

## Install

In Unity Package Manager, choose **Add package from git URL** and use a tagged release:

```text
https://github.com/prototir/unity-sdk.git#v0.1.0
```

For testing before the first tag is published, omit `#v0.1.0`. Pin a tag in production so an SDK
update cannot change an existing project unexpectedly.

After installation:

1. Open **Prototir > Project Setup**.
2. Select **Fix all available**, then resolve any remaining blocking items.
3. Import **Basic Integration** from the package's Samples tab if you want a small API example.
4. Build a non-development Web release and ZIP the contents of the export directory.

## Basic use

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

The package checks the installed Web module, active target, scenes, native threads, Run In
Background, development and profiling flags, debug symbols, PWA output, player template, data
caching, and product name. Blocking issues stop Web builds; recommendations remain visible without
silently changing the project.

**Fix all available** changes only settings with a single safe value. It also installs the included
edge-to-edge Web template, which removes Unity's default page frame and duplicate fullscreen UI.
Every successful Web build receives a root `prototir.json`; create a customized source manifest
through **Prototir > Create or Open Project Manifest** when the generated defaults are not enough.

Run the structural validator outside Unity with:

```bash
node tools/export-validator.mjs /path/to/export
```

## Editor behavior

Editor and non-Web play mode use local mocks and never import browser-only symbols. Storage is kept
in memory, readiness/events/scores are exposed as mock events, and managed AI requires an explicit
`PrototirSdk.MockAiHandler`. This keeps local tests deterministic and prevents accidental service
requests.

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
stays out of the way. Anywhere else it shows the Prototir mark, which opens a menu with
**Screenshot & comment**, **Comments** and **Open on Prototir** - the same menu testers see in a
web build, so the experience does not change between the two.

Screenshots are taken with `ScreenCapture.CaptureScreenshotAsTexture` after `WaitForEndOfFrame`, so
they match what the player saw. While the panel is open the component clears
`WebGLInput.captureAllKeyboardInput`, otherwise the game would swallow the tester's typing.

The component is inert outside Web builds and in the Editor. There is nothing to remove for a native
build, but testers will not see the button there.

### Posting from a downloaded build

A native build has no Prototir session, and providers like Google refuse to sign in inside an
embedded browser. So the build sends the tester to a real one: it shows a short code and a QR, the
tester approves at `prototir.com/link` on their desktop or phone, and the build receives a token
scoped to that one prototype.

They approve once per machine, not once per comment, and the screenshot they were writing is kept
and posted the moment they come back. Testers can disconnect any build from their Prototir account
settings.

Set `ApiBase` and `PrototypeSlug` to enable it. Without them the panel saves review files instead,
which needs no account and works offline.

On Prototir the feedback becomes an ordinary comment on the prototype, after Prototir's own
confirmation dialog. In a Web build you host yourself the panel saves a
`feedback.prototir-review.json` file that the tester sends you and you reload with **Import review**.
See the [Web SDK README](https://github.com/prototir/web-sdk#screenshot-feedback) for the file format
and its limits.

## Documentation and examples

- [Package documentation](Documentation~/index.md)
- [Unity examples](https://github.com/prototir/unity-examples)
- [Creator documentation](https://prototir.com/docs/creators?runtime=unity#setup)
- [Release process](DISTRIBUTION.md)

## Validate the package

```bash
node tools/test.mjs
```

The Node check validates package structure, bridge behavior, the project assistant, export
validator, and Web template. A tagged release must additionally compile in Unity 6 and pass a real
Prototir sandbox play test.

## License

[MIT](LICENSE.md)
