# AV Matrix Studio 2.0.1

## Visible client checkout

- Moves Master sign-in, account management, client checkout, check-in, and release
  into a dedicated **Master access & client checkout** row.
- Corrects the Google Drive layout that could place **Check out client** beneath
  the visible status card.
- Styles **Check out client** as a primary action in both sync workflows.

## Signed-in identity and Owner administration

- Retains the active Master Matrix session in memory while the application remains
  open, scoped to the linked file-share or Google Drive master.
- Shows a bottom-right sign-in notification with the user's display name and role.
- Shows the current signed-in identity in the lower-right application status bar.
- Adds **Admin — manage users** to the bottom-left sidebar for Owner accounts only.
- Routes the Admin shortcut directly to the account manager for the master where
  the Owner signed in.
- Clears the in-memory session when that master is unlinked, replaced, or its
  Google Drive connection is disconnected.
