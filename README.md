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
