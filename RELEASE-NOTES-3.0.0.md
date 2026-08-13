# AV Matrix Studio 3.0.0

## Simplified startup and login

- Opens on a Master Matrix welcome screen.
- Shows local/file-share, Google Drive, and Create new Master File options.
- Uses one app-wide Owner, Tech, or Read-only login for the session.
- Removes recurring Master Matrix file-unlock prompts after sign-in.
- Adds an explicit Log out action that returns to the welcome screen.
- Offers to merge/push or check in unsynchronized work before logout.

## Account-unlocked encryption

- New masters are encrypted with one random master key.
- Each account password wraps its own copy of that key using a salted PBKDF2 key
  and authenticated AES-GCM encryption.
- Different users can unlock the same `.avmatrix` with their own passwords.
- All roles can see the access list and change their own password.
- Owners retain full user management.
- Older masters have a one-time Owner migration path.

## Client-card checkout and safe merge

- Checkout and check-in actions are attached directly to each client card.
- Cards identify checkout holders and keep the explicit takeover warning.
- Checkout keeps the whole client directory visible and loads configuration-file
  bytes only for the selected client.
- Full-master merges preserve all checked-out clients exactly as stored remotely
  and merge only clients that are not checked out.

## Validation

- Windows-targeted Release build: 0 warnings, 0 errors.
- Round-trip tests cover Owner/Tech account unlocking with different passwords,
  self-service password changes, full-directory checkout, and locked-client merge preservation.
