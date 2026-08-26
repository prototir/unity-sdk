# Prototir SDK for Unity

Install the package from its Git URL in Unity Package Manager. The first supported target is a
Unity 6 Web release build using the standard, single-threaded Prototir runtime.

```csharp
using Prototir;

PrototirSdk.Ready();
PrototirSdk.Event("level_complete", new LevelEvent { level = 2 });
PrototirSdk.Score(1200);
await PrototirSdk.StorageSetAsync("difficulty", "hard");
var difficulty = await PrototirSdk.StorageGetAsync("difficulty");
```

`Ready()` must describe a genuinely interactive scene, not merely the first loader frame. Event
names are normalized to lowercase and must use letters, numbers, `_`, `.`, `:`, or `-`.

## Editor and non-Web behavior

No browser symbols are imported outside a real Unity Web player. Storage uses an in-memory mock,
signals are exposed through `MockReadySent`, `MockEventSent`, and `MockScoreSent`, and managed AI
requires an explicit `MockAiHandler`. This keeps tests deterministic and prevents an Editor scene
from accidentally contacting a production service.

## Pointer lock and Escape

Request pointer lock from a direct player action. When the browser releases it after Escape, pause
the scene and show a visible resume action. Never simulate pointer lock by hiding the cursor.

## Export profile

- Unity 6 Web release build.
- Native multithreading off.
- **Run In Background** on. Prototir's fullscreen, restart, and other shell controls live outside
  the Unity iframe; keeping the player active prevents focus changes from leaving the WebGL canvas
  black or frozen.
- PWA/service worker off.
- External analytics, Addressables, APIs, and fonts off or bundled locally.
- Uncompressed, Gzip, Brotli, or Unity decompression-fallback output may be uploaded; Prototir will
  verify the generated metadata rather than trusting filenames alone.
- Preserve the generated loader/framework/WASM/data files; do not run generic obfuscation over them.
- Use the SDK's **Prototir** Web template for an edge-to-edge canvas without Unity's default white
  frame, footer, project title, or duplicate fullscreen button. A deliberately custom project
  template remains possible, but its document and canvas must fill the viewport.

Run `node tools/export-validator.mjs <export-folder>` for a local structural preflight.

## Project setup assistant

Open **Prototir > Project Setup** before the first build. The SDK checks the installed Web module,
active target, enabled scenes, threads, **Run In Background**, development and profiling flags,
debug symbols, PWA template, edge-to-edge player template, Unity data caching, and product name.
Blocking issues stop a Web build; recommendations remain visible without preventing export.
**Fix all available** only changes settings for which the supported Prototir profile has an
unambiguous value. It enables **Run In Background**, installs the bundled Prototir template into
`Assets/WebGLTemplates/Prototir`, and selects it for Web builds. Installing Unity's Web Build
Support module and choosing a product name remain explicit user actions.

The preflight runs again whenever a Web build starts, so command-line and CI exports receive the
same protection as builds started from the Editor window.

## Generated `prototir.json`

Every successful Unity Web build receives a root `prototir.json`. By default, the SDK generates a
game manifest for desktop and mobile using `PlayerSettings.productName` and the exact Unity editor
version. To customize its name, type, devices, orientation, permissions, XR intent, or other
portable fields, choose **Prototir > Create or Open Project Manifest** and edit
`Assets/Prototir/prototir.json`. The project manifest is copied over the generated defaults on every
build, so stale output metadata cannot survive a rebuild.
