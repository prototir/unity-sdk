# Releasing the Unity SDK

GitHub is the canonical source and release history. Each release uses the same semantic version in
`package.json`, `CHANGELOG.md`, the Git tag, and the GitHub release.

## Release checklist

1. Run `node tools/test.mjs`.
2. Import the package into a clean supported Unity 6 project.
3. Run **Prototir > Project Setup** and build a non-development Web release.
4. Run `node tools/export-validator.mjs <export-folder>`.
5. Upload and play the export on Prototir with no blocking console or publication diagnostics.
6. Update `CHANGELOG.md`, commit, create the immutable `vX.Y.Z` tag, and publish a GitHub release.
7. Update documentation links only after the tag is available.

The Unity Package Manager Git URL is the primary installation channel. A future Unity Asset Store
listing should point to the same reviewed package and version rather than maintaining a separate
implementation branch.
