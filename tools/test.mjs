import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import vm from 'node:vm';
import { validateUnityExport } from './export-validator.mjs';

const root = mkdtempSync(join(tmpdir(), 'prototir-unity-'));
try {
  mkdirSync(join(root, 'Build'));
  writeFileSync(join(root, 'index.html'), '<canvas id="unity-canvas"></canvas>');
  writeFileSync(join(root, 'Build', 'game.wasm.br'), 'fixture');
  writeFileSync(join(root, 'Build', 'game.data.br'), 'fixture');
  writeFileSync(join(root, 'prototir.json'), JSON.stringify({ runtime: { engine: 'unity', profile: 'standard' } }));
  assert.deepEqual(validateUnityExport(root), []);
  writeFileSync(join(root, 'service-worker.js'), 'fixture');
  assert.match(validateUnityExport(root).join('\n'), /service-worker/);

  const packageManifest = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
  assert.equal(packageManifest.name, 'com.prototir.sdk');
  assert.equal(packageManifest.unity, '6000.0');

  const buildValidator = readFileSync(new URL('../Editor/PrototirBuildValidator.cs', import.meta.url), 'utf8');
  for (const token of ['WriteManifest(report.summary.outputPath)', 'Assets/Prototir/prototir.json', 'engine = "unity"', 'engineVersion = Application.unityVersion'])
    assert.ok(buildValidator.includes(token), `build validator missing ${token}`);
  const projectSetup = readFileSync(new URL('../Editor/PrototirProjectSetup.cs', import.meta.url), 'utf8');
  for (const token of ['Prototir/Project Setup', 'Fix all available', 'threadsSupport', 'runInBackground', 'Run In Background is disabled', 'debugSymbolMode', 'dataCaching', 'Web Build Support is missing', 'PROJECT:Prototir', 'InstallAndSelectWebTemplate'])
    assert.ok(projectSetup.includes(token), `project setup missing ${token}`);
  assert.match(
    projectSetup,
    /if \(!PlayerSettings\.runInBackground\)[\s\S]*?PrototirIssueSeverity\.Error,[\s\S]*?PlayerSettings\.runInBackground = true/,
    'Run In Background must be a blocking issue with a safe automatic fix'
  );
  const webTemplate = readFileSync(new URL('../Editor/WebGLTemplates/Prototir/index.html', import.meta.url), 'utf8');
  for (const token of ['width: 100%', 'height: 100%', '{{{ LOADER_FILENAME }}}', '{{{ DATA_FILENAME }}}', '{{{ FRAMEWORK_FILENAME }}}', '{{{ CODE_FILENAME }}}'])
    assert.ok(webTemplate.includes(token), `web template missing ${token}`);
  assert.ok(!webTemplate.includes('unity-footer'), 'web template must not add Unity chrome');

  const bridge = readFileSync(new URL('../Runtime/Plugins/WebGL/Prototir.jslib', import.meta.url), 'utf8');
  for (const token of ["source: 'prototir'", 'version: 1', "type: 'ready'", "type: 'event'", "type: 'score'", "type: 'storage'", "type: 'ai'"])
    assert.ok(bridge.includes(token), `bridge missing ${token}`);
  assert.ok(bridge.includes("event.source !== window.parent"));

  const sent = [];
  const callbacks = [];
  let installedLibrary;
  const parent = { postMessage: (message, origin) => sent.push({ message, origin }) };
  const context = {
    LibraryManager: { library: {} },
    mergeInto: (_target, source) => { installedLibrary = source; },
    window: {
      parent,
      location: { href: 'https://game.prttr.com/?prototir_origin=https%3A%2F%2Fprototir.com' },
      addEventListener: (_type, callback) => callbacks.push(callback)
    },
    URL,
    URLSearchParams,
    JSON,
    UTF8ToString: (value) => value,
    SendMessage: (...args) => sent.push({ callback: args })
  };
  vm.runInNewContext(bridge, context);
  context.PrototirBridge = installedLibrary.$PrototirBridge;
  installedLibrary.Prototir_Install();
  installedLibrary.Prototir_Ready();
  installedLibrary.Prototir_Event('level_complete', '{"level":2}');
  installedLibrary.Prototir_Score(1200);
  assert.deepEqual(
    sent.slice(0, 3).map((entry) => JSON.parse(JSON.stringify(entry))),
    [
      { message: { type: 'ready', source: 'prototir', v: 1 }, origin: 'https://prototir.com' },
      { message: { type: 'event', name: 'level_complete', data: { level: 2 }, source: 'prototir', v: 1 }, origin: 'https://prototir.com' },
      { message: { type: 'score', value: 1200, source: 'prototir', v: 1 }, origin: 'https://prototir.com' }
    ]
  );
  callbacks[0]({ source: parent, data: { source: 'prototir', v: 1, type: 'storage:result', id: 4, value: 'hard' } });
  assert.deepEqual(sent.at(-1).callback.slice(0, 2), ['__PrototirBridge', 'OnPrototirStorageResult']);
  // Where a downloadable build talks to Prototir. This URL is compiled into executables that can
  // never be updated, so a slip back to a host that serves no /api, or to the generated Azure
  // hostname that changes if the app is recreated, has to fail here rather than in the field.
  const settings = readFileSync(
    new URL('../Runtime/Native/Unity/PrototirSettings.cs', import.meta.url),
    'utf8',
  );
  const defaultBase = /DefaultApiBaseUrl = "([^"]+)"/.exec(settings)?.[1];
  assert.ok(defaultBase, 'PrototirSettings has no DefaultApiBaseUrl');
  assert.ok(
    !/^https:\/\/prototir\.com\//.test(defaultBase),
    'prototir.com serves no /api path; the API has its own hostname',
  );
  assert.ok(
    !/azurewebsites\.net/.test(defaultBase),
    'the generated Azure hostname changes if the app is recreated',
  );

  // The name of the file Prototir injects at upload. The server picks the name and the SDK reads
  // it, in different repositories, and a disagreement between them is silent: the build simply
  // never finds its slug and the creator is told to configure it by hand, exactly as before the
  // feature existed. Pinning the literal here is what turns that into a failing build.
  assert.match(
    settings,
    /InjectedFileName = "prototir-prototype\.json"/,
    'the injected slug file must be named prototir-prototype.json, which is what the API writes',
  );

  // Reporting is gated on a token that does not exist until pairing is approved, so every flush
  // and every drain before that point gives up immediately. If approval does not re-trigger them,
  // the session the tester just finished waits for the next 30s heartbeat, and a tester who quits
  // inside that window has it written to the queue and sent only on the following launch. That is
  // a creator watching their first play never arrive, and it is invisible from the code: the same
  // omission has already shipped twice here, once for the queue at boot and once for Configure.
  const runtime = readFileSync(
    new URL('../Runtime/Native/Unity/PrototirNativeRuntime.cs', import.meta.url),
    'utf8',
  );
  const approved = runtime.slice(
    runtime.indexOf('if (result.Outcome == PrototirPairingOutcome.Approved)'),
    runtime.indexOf('_state = result.Outcome == PrototirPairingOutcome.Cancelled'),
  );
  assert.ok(approved.length > 0, 'the approved branch of BeginPairingAsync moved');
  assert.match(
    approved,
    /SendPendingAsync\(/,
    'approving a pairing must drain the queue: it is the first moment those sessions can be sent',
  );
  assert.match(
    approved,
    /FlushAsync\(/,
    'approving a pairing must report the session in progress rather than wait for a heartbeat',
  );

  const pairingScreen = readFileSync(
    new URL('../Runtime/Native/Unity/PrototirPairingScreen.cs', import.meta.url), 'utf8');
  assert.match(pairingScreen, /^#if !UNITY_WEBGL \|\| UNITY_EDITOR/);
  assert.match(pairingScreen, /PrototirQrSvg\.TryDecode\(svg, out var modules\)/);
  assert.match(runtime, /public static event Action<PrototirPairingRequest> PairingStarted/);
  const sdk = readFileSync(new URL('../Runtime/PrototirSdk.cs', import.meta.url), 'utf8');
  assert.match(sdk, /public static void ShowPairingScreen\(\)/);
  assert.match(sdk, /#if !UNITY_WEBGL \|\| UNITY_EDITOR\s+Native\.PrototirPairingScreen\.Show\(\)/);
  const unityHttp = readFileSync(
    new URL('../Runtime/Native/Unity/PrototirUnityNative.cs', import.meta.url), 'utf8');
  assert.match(
    unityHttp,
    /PostJsonAsync\([\s\S]*?PrototirNativeTicker\.RunOnMainThread\(\(\) => SendOnMainThreadAsync\(/,
    'UnityWebRequest must start on the player loop after protocol awaits move to a worker',
  );
  const ticker = readFileSync(
    new URL('../Runtime/Native/Unity/PrototirNativeTicker.cs', import.meta.url), 'utf8');
  assert.match(
    ticker,
    /private void Awake\(\) => PrototirNativeRuntime\.InitializeStorage\(Application\.persistentDataPath\)/,
    'persistentDataPath must be captured on Unity’s main thread before pairing can resume on a worker',
  );
  assert.doesNotMatch(runtime, /Application\.persistentDataPath/,
    'async runtime code must use the captured storage paths instead of reading Unity’s path on a worker');
  assert.match(unityHttp, /private readonly string _directory;/,
    'the token store must retain its directory instead of looking it up during async writes');
  assert.match(unityHttp, /return Path\.Combine\(_directory, safe \+ "\.token"\);/,
    'token paths must use the captured directory after approval resumes on a worker');

  console.log('Unity package and protocol checks passed.');
} finally {
  rmSync(root, { recursive: true, force: true });
}
