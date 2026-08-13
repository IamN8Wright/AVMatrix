# AV Matrix Studio 3.2.0

- Redesigned the About card with the AV Matrix icon above and InN8 Labs branding
  in the lower-left half.
- Added a verified self-updater tied to the fixed InN8 Labs Google Drive release.
- The updater accepts a single EXE or release ZIP, verifies product identity,
  version, executable structure, and SHA-256, retains a rollback copy, and
  relaunches after installation.
- Added near-real-time Google Drive coauthoring for inventory metadata.
- Independent edits merge automatically and same-field collisions keep the
  current local editor's value.
- Live coauthoring pauses during client checkout; configuration-file contents
  remain checkout-only.
- Preserved Owner, Tech, Read-only, per-client assignment, and checkout rules.
