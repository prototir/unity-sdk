# Prototir Web manifest example

The Project Setup assistant now installs and selects the SDK's production Web template directly.
It produces an edge-to-edge canvas without Unity's default frame or duplicate player controls.
This sample retains a portable `prototir.json` example for projects that need to customize their
published metadata.

The ZIP root must contain `index.html`, `prototir.json`, and Unity's generated folders. Use a release
build with native multithreading and PWA support disabled. Do not add remote analytics,
Addressables, fonts, or APIs; the standard Prototir sandbox intentionally blocks arbitrary network
origins.
