# AV Matrix Studio 1.0.0

Created by InN8 Labs — 2026

## Security and portability

- Optional password protection for backup, restore, file-share master, and Google Drive master workflows.
- Password protection automatically uses compact JWE (`PBES2-HS256+A128KW` + `A256GCM`).
- Protected files are auto-detected; wrong passwords and damaged files fail before current data is replaced.
- Passwords are never stored in `.avmatrix` content or application settings.
- Older unprotected `.avmatrix` files remain supported.

## Equipment interfaces

- Equipment starts with one IP/MAC interface and can add or remove interface rows.
- Types: Main, Control, Dante, AVB, CobraNet, and AES67.
- Legacy Primary, Secondary, Target, Dante, and MAC fields migrate automatically.
- MAC formats are normalized to uppercase colon notation.
- Each interface appears as an indented equipment-grid row.

## Verification

- Each entered IP is pinged from the selected NIC only when Verify is pressed.
- Local-subnet MAC identity is checked through Windows ARP/neighbor resolution.
- Routed/VLAN destinations are not compared to gateway MACs.
- Ports 80 and 443 are probed; a web-portal button appears only after a port responds.
- Device LED aggregation: gray no IP, blue waiting, red none reachable, green all reachable, amber mixed, yellow MAC mismatch.

## Collaboration

- Existing controlled file-share/synced-folder pull and push remains available.
- Direct Google Drive online sync accepts an editable Drive share link.
- OAuth Desktop credentials are imported once; Google secrets and tokens are stored in Windows Credential Manager.
- Both collaboration modes retain stale-push protection and pre-pull recovery backups.

## Excel and branding

- IP Matrix export contains one row per network interface.
- Master Register includes all interfaces, observed MACs, and detected portal URLs.
- About credit updated to InN8 Labs with revision 1.0.0 and the current year.

## Build note

Run `Build-Windows.bat` on a Windows PC with the .NET 8 SDK. Direct Google Drive additionally requires an OAuth Desktop client JSON from the InN8 Labs Google Cloud project; setup steps are in `README.md`.
