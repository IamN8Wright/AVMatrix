# AV Matrix Studio 2.0.0

## Master Matrix accounts

- Adds Master Matrix sign-in with Owner, Tech, and Read-only roles.
- New masters require an initial Owner; existing masters can be upgraded in place.
- Owners can add, edit, disable, reset, and delete accounts.
- Passwords are stored as PBKDF2-SHA256 hashes with random salts and 310,000
  iterations; readable account passwords are never written to the master.
- Read-only workspaces block local inventory changes, and the sync layer separately
  rejects master writes from Read-only sessions.

## Client checkout and sub-matrices

- Full-master pull remains available but retrieves lightweight client/device
  inventory and configuration-file metadata only.
- Check out a client to acquire its exclusive lock and pull its complete sub-matrix,
  including configuration-file bytes.
- Check in & push updates the client sub-matrix, updates master metadata, releases
  the lock, and returns to the full metadata workspace.
- Locks display the holder, machine, and checkout time.
- Owner and Tech accounts can boot a lock after an explicit prompt instructing them
  to confirm with the current technician that work was pushed.
- A booted holder cannot push over the new checkout owner.

## Device configuration files

- Adds a Configuration files tab to the device editor.
- Supports multi-file attachment, replacement, removal, save-a-copy, SHA-256
  verification metadata, file size, timestamp, and uploader identity.
- Supports files up to 250 MB each, with an additional confirmation over 50 MB.
- Adds a sortable Config Files count to the equipment grid and includes attached
  filenames in equipment search.

## Storage layout

- File shares store client payloads in `<master-name>.clients/<client-id>.avclient`.
- Google Drive stores each client payload as a separate `.avclient` file, records
  its Drive file ID in the master, and copies direct master sharing permissions to
  new sub-matrices.
- The main `.avmatrix` never receives configuration-file bytes during full sync.
- Existing `.avmatrix` formats remain readable. Transfer format 4 and local schema
  5 add accounts, locks, sub-matrix references, and attachment metadata.
- JWE file protection continues to cover the master and its client sub-matrices.

## Compatibility and safety

- Existing 1.x masters prompt for first-Owner setup when authentication is first used.
- Existing three-way Merge & push behavior remains available for full-master metadata.
- Merge, push, and checkout honor exclusive client locks.
- Recovery copies are created before client checkout replacement, check-in, and
  merged master writes.
