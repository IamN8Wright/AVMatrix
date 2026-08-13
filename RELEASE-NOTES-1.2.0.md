# AV Matrix Studio 1.2.0

## Collaborative merge

- Replaces stale-overwrite blocking with a baseline-aware **Merge & push** workflow.
- Supports both shared/UNC `.avmatrix` files and direct Google Drive online masters.
- Combines independent changes throughout the full hierarchy: clients, locations,
  rooms, equipment, and typed IP/MAC interfaces.
- Preserves additions and removals made by different technicians when they do not
  overlap.
- Shows a side-by-side review when the same field or record was changed differently.
  The technician chooses whether this PC or the master wins for those overlaps;
  unrelated merged work remains intact.
- Saves the previous remote file as a recovery copy before writing a merged master.
- Rechecks Google Drive immediately before and after upload and retries a rapidly
  changing master up to three times.

## Sync status indicator

- Green sync icon: this PC matches the last synchronized content and the linked
  master has no detected changes.
- Amber sync icon: the master is not linked, local edits need to be pushed, or
  remote changes need to be merged.
- File-share status is checked periodically. Google Drive status is updated whenever
  the online master is inspected, pulled, or pushed.

## Upgrade note

Existing links automatically establish a merge baseline when the app can verify
that local and master data match. If they do not match, pull once before starting
new collaborative edits. The app will not guess a baseline or silently overwrite
data.
