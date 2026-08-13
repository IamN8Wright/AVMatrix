AV Matrix Studio 3.2.6 — Windows x64

1. Extract the release ZIP to a normal folder.
2. Double-click AVMatrixStudio.exe.
3. Choose a client card.
4. Choose the adapter under Verify from.
5. Press Verify devices when you want to check reachability, MAC identity, and web ports.

The program does not verify automatically. Device colors are green when every
entered IP replies, amber when only some interfaces are green, red when none
reply, yellow for a reachable local interface with a MAC mismatch, blue while
waiting, and gray when no IP exists. Devices show as one row by default. Click a
device once to show or hide its IP interfaces; double-click it to edit.

Use Add IP / MAC in the equipment Network tab for Main, Control, Dante, AVB,
CobraNet, or AES67 interfaces. Common MAC formats are accepted and saved as
uppercase colon notation.

Excel import now stops at a review screen before changing data. It lists every
new device, merge, unchanged duplicate, ambiguous match, skipped row, and sheet
whose equipment headers were not recognized. Workbook row totals are reconciled
so omitted items are visible before approval. Existing device values take
priority; approved merges fill blank fields and add new network interfaces.

The app starts on a Master Matrix welcome screen. Choose a local/file-share or
Google Drive master, or create a new Master File, then sign in once with an Owner,
Tech, or Read-only account. Account passwords unlock the encrypted master for the
app session; there is no recurring separate file-unlock prompt.

Every client card contains its own checkout action. A full merge skips clients
that are currently checked out and merges only the available clients. Log out
offers to merge/check in unsynchronized work before returning to the login screen.

Client cards and open client workspaces show Checked in, Checked out to you, or
the name of the current checkout holder. A progress window remains visible while
checkout/check-in transfers the client sub-matrix.

All signed-in users can see who has access and change their own password. Owners
can also add, edit, disable, reset, and delete accounts, and assign Tech or
Read-only users to all clients or selected individual clients.

Use Refresh NICs beside Verify from after connecting, disconnecting, or changing
a network adapter.

Direct Google Drive requires an OAuth Desktop client JSON from the InN8 Labs
Google Cloud project. Import it in Shared Sync > Google Drive online, sign in,
then paste the share link for an editable .avmatrix file. Google tokens are
stored in Windows Credential Manager.

Google Drive metadata is synchronized in the background about every five seconds
while the app is signed in and no editor dialog is open. Same-field collisions
keep the current user's edit; independent edits are merged. During checkout, the
background connection continues checking lock ownership so a takeover is shown
promptly without exposing configuration-file payloads to coauthoring.

The About page includes Check for app update. It downloads the fixed InN8 Labs
Google Drive release, verifies the product name, version, executable format, and
SHA-256, keeps a rollback copy, installs after this instance closes, and relaunches.

Revision 2.0.0 adds embedded device configuration files, authenticated Master Matrix
accounts, and exclusive client checkout. A normal full-master pull retrieves all
client and device inventory plus configuration-file metadata only. Check out a client
to download and edit its actual configuration files, then use Check in & push to save
its sub-matrix and release the lock. Owner accounts manage Tech and Read-only logins.
If a checkout was abandoned, another Owner or Tech can boot it after confirming they
asked the current holder whether changes were pushed.

Revision 2.0.1 places Master sign-in, account, checkout, check-in, and release
controls in a dedicated visible row. After Master Matrix sign-in, the app confirms
the display name and role at the bottom-right and keeps that identity in the status
bar. Owners also receive an Admin — manage users button in the lower-left sidebar.

On file shares, keep the <master-name>.clients folder beside the .avmatrix master.
On Google Drive, client sub-matrices are separate .avclient files whose IDs and share
permissions are managed by the app. Keep the master file-protection password unchanged
after client sub-matrices exist.

Revision 1.2.0 added baseline-aware Merge & push for file-share and Google Drive
masters. Independent changes to clients, locations, rooms, equipment, and IP/MAC
entries combine automatically. If the same field changed on both sides, review the
side-by-side values and choose which side wins for the overlaps. The sidebar sync
icon is green when synced and amber when changes need attention. Existing matching
links establish their baseline automatically; otherwise pull once before beginning
collaborative edits. Revision 1.1.3 displays equipment access passwords as readable,
copyable text in the device editor; encrypted .avmatrix password prompts remain masked. Revision
1.1.2 places Web Portal beside Verification and adds stronger alternating row
shading. Revision 1.1.1 improves the Welcome and sidebar-brand alignment and
fixes the clipped client-card Edit button. Revision 1.1.0 provides a cleaned Excel export:
the client name is used for the file and Project Summary banner; the summary lists
locations and rooms; and each location receives its own equipment tab. Google sign-in must be
completed before connecting a Drive share link.

Pull once to establish a baseline. Use Merge & push for full-master metadata work;
independent changes combine automatically and overlapping fields require an explicit
choice. For configuration-file work, use Check out client and Check in & push.
Recovery copies are created around pulls, merges, checkout replacement, and check-in.

For Excel, use a client card's Excel button or Export Excel in its workspace.
The resulting .xlsx includes Project Summary plus one clean device tab per
location. Multiple typed IP and MAC addresses stay together on the device row.

Equipment and batches can be dragged onto Rooms. Rooms can be dragged onto
Locations. Searchable Move dialogs are available from the context menus.

The program is self-contained and does not require a separate .NET installation.
Windows may show SmartScreen because this development build is not code-signed.

Application data is saved under:
%LOCALAPPDATA%\AVMatrixStudio

Created by InN8 Labs.
