# AV Matrix Studio Release Process

1. Update the application version.
2. Build and publish the Windows application.
3. Produce a SHA-256 checksum for the release asset.
4. Create a GitHub Release whose tag matches the application version, for example `v4.0.0`.
5. Attach the Windows release package and checksum/update metadata.
6. AV Matrix Studio checks the latest GitHub Release, compares semantic versions, downloads the release asset, verifies SHA-256, and hands installation to an updater process.

The exact GitHub Actions workflow will be added after the current solution/project files are present so the build command, runtime identifier, framework, packaging layout, and executable name are taken from the real application rather than guessed.
