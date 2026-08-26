import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';

const generatedSuffixes = (extension) => [extension, `${extension}.gz`, `${extension}.br`, `${extension}.unityweb`];

export function validateUnityExport(root) {
  const errors = [];
  if (!existsSync(root)) return [`Export folder does not exist: ${root}`];
  const files = walk(root);
  const normalized = files.map((file) => relative(root, file).replaceAll('\\', '/'));
  if (!normalized.includes('index.html')) errors.push('index.html must exist at the ZIP root.');
  if (!normalized.some((file) => generatedSuffixes('.wasm').some((suffix) => file.toLowerCase().endsWith(suffix))))
    errors.push('A Unity .wasm payload is required.');
  if (!normalized.some((file) => generatedSuffixes('.data').some((suffix) => file.toLowerCase().endsWith(suffix))))
    errors.push('A Unity .data payload is required.');
  if (normalized.some((file) => file.toLowerCase().endsWith('.map')))
    errors.push('Source maps/debug symbols are not accepted.');
  if (normalized.some((file) => file.toLowerCase().endsWith('service-worker.js')))
    errors.push('PWA/service-worker output is outside the standard profile.');

  const manifestPath = join(root, 'prototir.json');
  if (existsSync(manifestPath)) {
    try {
      const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
      if (manifest.runtime?.engine !== 'unity') errors.push('prototir.json runtime.engine must be unity.');
      if (manifest.runtime?.profile && manifest.runtime.profile !== 'standard')
        errors.push('Only the standard runtime profile is currently supported.');
    } catch {
      errors.push('prototir.json must contain valid JSON.');
    }
  }
  return errors;
}
function walk(root) {
  return readdirSync(root, { withFileTypes: true }).flatMap((entry) => {
    const path = join(root, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}

if (process.argv[1]?.endsWith('export-validator.mjs') && process.argv[2]) {
  const errors = validateUnityExport(process.argv[2]);
  if (errors.length) {
    console.error(errors.join('\n'));
    process.exitCode = 1;
  } else {
    console.log('Unity Web export matches the Prototir standard profile.');
  }
}
