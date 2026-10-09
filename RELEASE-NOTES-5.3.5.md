# InNasc 5.3.5

- Unavailable sync buttons now use a muted background and a normal pointer, instead of retaining the bright enabled blue/red appearance.
- Sync dialogs explain why actions are unavailable: Google sign-in, a missing company link, connection setup, an operation in progress, or a checkout on the other backend.
- Check-in, release and recovery controls appear only for an active checkout on the dialog's backend.
- Button labels display the literal ampersand in **Check in & push** and **Merge & push**.
- Connection-only Google Drive setup disables all publishing and recovery controls.

Google sign-in and company login remain separate. Sync actions require an authorized connection; this change does not bypass checkout ownership or publishing permissions. The real 20-minute connected cloud idle/reconnect test remains pending.
