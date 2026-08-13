# AV Matrix Studio Desktop

AV Matrix Studio is a native Windows inventory and verification application created by InN8 Labs. It organizes records as:

**Client → Location → Room → Equipment → Network interfaces / Configuration files**

The client card deck and equipment grid are searchable and sortable. Rooms and batches of equipment can be moved between containers with drag-and-drop or searchable move dialogs. Client data exports to a real, validated `.xlsx` workbook.

## Revision 3.2.6

Revision 3.2.6 makes equipment labels permanent and Excel import reviewable
before any client data changes.

- Equipment-field titles are always visible above their boxes and aligned to
  the left edge of each input.
- Excel import scans all worksheets and previews every New, Merge, Duplicate,
  Ambiguous, Skipped, and Sheet Skipped result.
- The preview reconciles non-empty source rows so unexplained omissions are
  visible before import.
- Merge rows show which blank fields and network interfaces will be added.
- Existing values continue to take priority; duplicates and ambiguous matches
  remain untouched.
- Background checkout ownership checks continue during a checkout so a takeover
  is visible without allowing configuration payload coauthoring.
- Lost checkout ownership cannot block sign-in, and the displaced local client
  copy is preserved.

## Revision 3.2.2

Revision 3.2.2 collapses network-interface detail rows and makes repeat Excel
imports enrich existing device records instead of blindly duplicating them.

- Equipment appears as one row by default. Click it once to show or hide its IP
  interfaces; double-click still opens the equipment editor.
- Excel import compares rows against devices throughout the selected client by
  equipment ID, serial number, MAC address, hostname, IP address, and—when
  unambiguous—manufacturer/model/description.
- Existing records take priority. Imported values fill only blank fields,
  including MAC address, username, password, notes, and other device details.
- New IP/MAC interfaces are added without replacing existing interfaces, while
  unmatched spreadsheet rows are added as new devices.
- Import results report new devices, merged devices, filled fields, and added
  interfaces.

## Revision 3.2.1

Revision 3.2.1 moves the client checkout and add-equipment actions into a
dedicated row directly above the equipment summary cards.

- **+ Add** aligns with the right edge of the **No IP** card.
- **Check out client** sits immediately to the left of **+ Add**.
- Both button labels remain centered at different window sizes and Windows
  display scales.

## Revision 3.2.0

Revision 3.2.0 adds a redesigned About card, a verified self-updater, and
near-real-time Google Drive coauthoring for inventory metadata.

- The AV Matrix Studio icon now occupies the upper brand position. The InN8
  Labs mascot appears in the lower-left half, with credits shifted right.
- **Check for app update** downloads the release stored at the fixed InN8 Labs
  Google Drive file ID. It accepts the published single EXE or a release ZIP,
  verifies the Windows executable, product identity, version, and SHA-256,
  retains a rollback copy, installs after shutdown, and relaunches.
- Google Drive sessions check for inventory changes about every five seconds.
  Independent changes merge automatically; same-field conflicts keep the local
  editor's current value.
- Read-only and per-client access rules remain enforced. Live coauthoring pauses
  during client checkout so actual configuration-file payloads remain protected
  by the exclusive client sub-matrix workflow.

## Revision 3.1.0

Revision 3.1.0 makes client checkout and client permissions explicit throughout
the application.

- Client cards and open client workspaces identify whether a client is checked
  in, checked out to the signed-in user, or held by another technician.
- Checkout, takeover, and check-in display an indeterminate progress window
  while the client lock, inventory, and configuration-file sub-matrix are
  transferred.
- Owners can assign Tech and Read-only accounts to all current/future clients or
  a selected list of individual clients. Unassigned clients are hidden and
  independently preserved from the Master during merge.
- Revoking client access, disabling a user, or making a checkout holder
  Read-only releases affected locks after an explicit Owner warning.
- The verification toolbar includes **Refresh NICs** while preserving the
  current adapter selection when it is still available.
- Equipment editor captions now appear as placeholder text inside each field.

Existing accounts default to all-client access so upgrading does not unexpectedly
hide clients.

## Revision 3.0.5

Revision 3.0.5 gives the 50 pt welcome title a DPI-safe layout row and
independent subtitle spacing, preventing the two lines from overlapping or
being clipped on scaled Windows displays.

## Revision 3.0.4

Revision 3.0.4 increases and vertically centers the bottom status bar so its
text remains fully visible with scaled Windows displays. The green/amber sync
indicator now follows the Master Matrix used by the active signed-in session
and refreshes as soon as the session or synchronization state changes.

## Revision 3.0.3

Revision 3.0.3 centers and enlarges the Master welcome experience, makes Google
Drive the primary sync route while retaining Local / file share as an option,
hides operational navigation until a Master user signs in, and fixes repeated
sign-in after logout by fully resetting the welcome form's enabled state.

## Revision 3.0.2

Revision 3.0.2 adds an explicit per-file **Download** link to the equipment
editor's Configuration files tab. Download is available only to Owner and Tech
accounts while the client is checked out and the actual sub-matrix payload is
present. Metadata-only and Read-only views explain why the action is unavailable.

## Revision 3.0.1

Revision 3.0.1 replaces the legacy N8Wright artwork on the About page with the
square InN8 Labs mascot logo. The artwork is trimmed, centered, and embedded in
the application so it remains crisp and available in both light and dark mode.

## Revision 3.0.0

Revision 3.0 replaces the multi-step file-unlock/sign-in workflow with one
app-wide Master Matrix login.

- The app opens on a dedicated welcome screen with local/file-share, Google Drive,
  and **Create new Master File** options plus the Master Matrix login fields.
- A successful Owner, Tech, or Read-only login unlocks the encrypted `.avmatrix`
  and remains active until **Log out**. The old recurring **Unlock AV Master File**
  step is not used for account-protected masters.
- New masters use one random encrypted master key. Every account password wraps
  its own copy of that key, so different users can unlock the same file without
  sharing a common file password.
- All roles can open **Access — users & password**, see who has access, and change
  their own password. Owners additionally add, edit, disable, reset, and delete users.
- Logout detects unsynchronized work and offers to merge and push—or check in the
  active client—before returning to the welcome/login screen.
- Checkout is tied directly to each client card. Cards show the current holder,
  check in the locally held client, and provide the existing explicit takeover warning.
- A full-master merge automatically preserves every checked-out client from the
  master and merges only clients that are not checked out.
- Checkout keeps the complete client directory visible while downloading actual
  configuration-file contents only for the selected client.
- Existing unprotected or older single-password masters remain readable. The first
  legacy upgrade is a one-time Owner migration; other legacy users may need an Owner
  password reset to receive their account-wrapped key.

## Revision 2.0.1

Revision 2.0.1 makes Master Matrix access easier to find and keeps the signed-in
identity available throughout the current app session:

- Both file-share and Google Drive sync windows now show a dedicated
  **Master access & client checkout** row. **Check out client** is a prominent
  primary action and no longer sits below the visible Google Drive status card.
- Successful Master Matrix sign-in shows a bottom-right confirmation with the
  display name and role. The same identity remains visible in the app status bar.
- Signing in as an Owner adds **Admin — manage users** to the lower-left sidebar.
  It opens the correct linked master's account manager directly.
- Owner, Tech, and Read-only sessions remain active in memory until the app closes,
  the linked master is changed, or the corresponding connection is disconnected.

## Revision 2.0.0

Revision 2.0 introduces authenticated Master Matrix workspaces, exclusive client
checkout, lightweight full-master pulls, and embedded device configuration files.

### Device configuration files

- Every device editor now includes a **Configuration files** tab for backups,
  presets, DSP files, switch configurations, and other device-specific files.
- Multiple files can be attached, removed, inspected by name/size/hash, and saved
  back to disk. Files up to 250 MB each are supported; files over 50 MB show a
  size warning before being embedded.
- The equipment grid shows the number of configuration files attached to a device,
  and configuration filenames are searchable.
- Standalone local projects can manage attachments directly. Once a master is
  linked, configuration-file contents are editable only while that client is checked out.

### Lightweight master and client sub-matrices

- A normal **Pull** retrieves all clients, locations, rooms, devices, network data,
  and configuration-file metadata—but not the configuration-file bytes.
- File contents live in one sub-matrix per client and are downloaded only by
  **Check out client**. This keeps the primary `.avmatrix` inventory lightweight.
- On a file share, sub-matrices are stored in a `<master-name>.clients` folder next
  to the master. Keep that folder with the `.avmatrix` file when moving or backing
  up the master.
- On Google Drive, sub-matrices are separate `AVMatrix-<master>-<client>.avclient`
  files. Their Drive IDs are stored in the master, and the app copies the master's
  direct sharing permissions when each sub-matrix is created.

### Checkout, check-in, and booting

- Checkout places an exclusive lock on a client and brings only that client's full
  data and configuration-file payloads into the local workspace.
- **Check in & push** writes the client sub-matrix, updates the lightweight master
  inventory, releases the lock, and returns the app to the all-client metadata view.
- Other technicians can see who holds a checkout, the computer name, and when it
  began. To prevent ghost locks, an Owner or Tech may explicitly boot the holder.
  The confirmation instructs them to ask the other technician in person whether
  changes were pushed before proceeding.
- A booted technician cannot overwrite the new holder. Their unpushed local data
  remains on their PC and recovery copies continue to be created around sync operations.

### Master Matrix accounts and roles

- New masters require creation of the first **Owner** account. Existing masters
  prompt for Owner setup the first time an authenticated operation is used.
- Owners can create, edit, disable, reset, and delete accounts from **Accounts**.
- **Owner** has complete access and account management.
- **Tech** can read, write, merge, pull, push, check out, check in, and boot a checkout.
- **Read-only** can inspect and pull inventory metadata; editing is blocked in the
  workspace and master writes are independently rejected by the sync service.
- Account passwords use PBKDF2-SHA256 with random salts and 310,000 iterations.
  Password hashes—not readable passwords—are stored in the master.

Revision 3.0 supersedes the separate shared file password: Master Matrix accounts
now unlock the encrypted master and its client sub-matrices for the app session.

Revision 1.2.0 added baseline-aware **Merge & push** for both file-share and direct
Google Drive masters. That merge workflow remains available for full-master metadata
collaboration and respects checked-out client locks.

Revision 1.2.0 details:

- The last pulled or pushed master is retained locally as the common merge baseline.
- Independent changes merge automatically at the client, location, room, device,
  and IP/MAC-interface levels. For example, one technician can update Client 1
  while another updates Client 2, and both sets of changes are preserved.
- If both technicians change the same field differently, a modern comparison
  window shows the item, field, this-PC value, and master value. The technician
  explicitly chooses which side wins for only the overlapping changes.
- The previous remote master is saved as a recovery copy before a merged write.
- Google Drive uploads recheck the online file immediately before and after the
  upload and retry when another update lands during that window.
- The sidebar sync icon is green when local data and the linked master are in
  sync, and amber when local or detected remote changes need to be merged.

Existing synchronized links are upgraded to a merge baseline automatically when
the app can confirm that local and master data still match. Otherwise, pull once
before beginning a new round of collaborative edits.

Revision 1.1.3 changed the device editor to display the equipment access password
as normal, copyable text so technicians can reference it when signing in to a
device. Password prompts used to protect encrypted `.avmatrix` files remain masked.

Revision 1.1.2 moved **Web Portal** directly after **Verification**
in the client equipment grid and increased alternating-row contrast. Device and
indented interface rows now share one consistent zebra-stripe sequence in light
and dark mode.

Revision 1.1.1 increased the Welcome heading, vertically centered the
sidebar brand text with the application logo, and corrected the clipped
client-card Edit button. The card export button now intentionally reads
**Excel** instead of showing a clipped `.xlsx` suffix.

Revision 1.1.0 redesigned the client-facing Excel workbook:

- The default filename, workbook title, and Project Summary A1 banner use the client name.
- Project Summary contains client information plus a complete location-and-room listing; the internal project name and verification statistics are omitted.
- Each location receives its own safely named worksheet with equipment grouped by room.
- Location sheets contain only Room, Description, Manufacturer, Hostname, Serial Number, Firmware, Primary IP, typed Secondary IPs, typed MAC Addresses, Subnet, Gateway, User Name, Password, and Notes.
- Legacy Target IP, status, timestamps, source, record IDs, observed MACs, and other internal columns are no longer included.
- Rows automatically grow to display multiple IP and MAC addresses without clipping.

Revision 1.0.1 fixed Google Drive share-link connection. The form now
preserves the pasted URL while starting the connection and enables **Connect
share link** only after Google sign-in is complete.

The major 1.0.0 revision added:

- Optional password protection for backups and shared `.avmatrix` masters. Selecting protection automatically encrypts the file as RFC 7516 compact JWE; there is no separate encryption switch.
- Automatic detection and password prompting when restoring, pulling, or importing a protected master. Older unprotected `.avmatrix` files remain compatible.
- A dynamic list of typed IP/MAC interfaces for each device: Main, Control, Dante, AVB, CobraNet, and AES67.
- MAC normalization for colon, hyphen, Cisco-dot, spaced, and undelimited formats. Saved values use uppercase colon notation.
- Per-interface verification rows, MAC comparison on the selected NIC's local IPv4 subnet, TCP 80/443 discovery, and conditional **Open** web-portal buttons.
- Direct Google Drive online pull/push using a share link, Google OAuth, revision fingerprints, and pre-pull recovery backups.
- InN8 Labs credit on the About page.

## Build the Windows program

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Double-click `Build-Windows.bat`.
3. Open `publish\win-x64\AVMatrixStudio.exe`.

The published program is a self-contained, single-file 64-bit Windows executable. The target PC does not need a separate .NET installation. Use `Run-Source.bat` while developing.

## Device network interfaces and verification

The Network tab starts with one IP/MAC row. Use the blue **Add IP / MAC** button for additional control, audio-network, or other interfaces. A changed address returns to Waiting until the next manual verification.

Choose an active adapter under **Verify from**, then press **Verify devices**. Verification never runs automatically at startup or on a timer.

- Green device: every entered IP replied.
- Amber device: at least one IP replied and another interface is not green.
- Red device: verification completed and none of its entered IPs replied.
- Yellow device or interface: the IP replied, but the locally observed MAC differs from the expected MAC.
- Blue: waiting for manual verification.
- Gray: no IP is entered.

Each interface is shown indented below its device. TCP ports 80 and 443 are tested during verification. **Open** appears only for an interface where one of those ports answered; HTTPS is preferred when both answer.

MAC verification uses the Windows neighbor/ARP result only for IPv4 destinations on the selected adapter's local subnet. Across a router or VLAN boundary, the app reports that the MAC could not be verified and does not compare the expected device MAC to a gateway MAC.

## Account-protected Master Matrix files and portable backups

New shared masters are encrypted automatically and unlocked by their Master Matrix
user accounts. Each account password is salted and hashed for authentication and
also derives a separate AES-GCM wrapping key for the shared random master key.

Settings still includes **Back up all data (.avmatrix)** and **Restore backup**.
Standalone portable backups can optionally use compact JWE with
`PBES2-HS256+A128KW` key management and `A256GCM` authenticated encryption.

Readable passwords are never written into the `.avmatrix` file or application
settings. The decrypted master key exists only in the signed-in app session.

Restore validates and decrypts the complete file before current client data is changed. A wrong password or damaged file leaves existing data untouched.

## Shared-master collaboration

Open the circular-arrows sidebar icon. The file-share workflow supports a Windows/UNC share or any locally synced folder. Direct Google Drive is available from **Google Drive online**.

The workflow is controlled pull/merge-and-push collaboration rather than live simultaneous editing:

1. Pull before starting work.
2. Make changes locally.
3. Push when finished.

Fingerprint checks reject a stale push if another person changed the master. Every pull creates a timestamped local recovery `.avmatrix` file first.

## Direct Google Drive setup

A share link alone cannot authorize software to write a Drive file. AV Matrix Studio therefore uses Google OAuth and the Drive API. The signed-in Google account must have edit access to the linked `.avmatrix` file.

An InN8 Labs Google Cloud project administrator must complete the one-time app setup:

1. Enable the Google Drive API.
2. Configure the OAuth consent screen and any required test users.
3. Create an OAuth client with application type **Desktop app**.
4. Download its client JSON.

On each PC, open **Shared master sync → Google Drive online**, import that OAuth JSON, and choose **Sign in with Google**. Paste the share link for an existing `.avmatrix` Drive file and connect it. OAuth secrets and tokens are saved in Windows Credential Manager, not in local project JSON, Excel exports, or `.avmatrix` files.

The app requests Drive access because a pasted share link may point to an existing file that the app did not create. Production distribution may require the Google Cloud project owner to complete Google's OAuth verification requirements.

## Excel support

**Import Excel** recognizes the supplied `HillCorp IP Matrix.xlsx` and `EQUIPMENT SERIAL_IP.xlsx` column styles plus common aliases. Existing fixed Primary, Secondary, Target, Dante, and MAC fields migrate into typed interface records.

Client **Excel** buttons create a real `.xlsx` with:

- Project Summary, identified by the client name and listing its locations and rooms
- One clean, client-facing equipment worksheet for each location

Each device occupies one row. Its Main address is shown as Primary IP; every additional typed address and all matching MAC addresses are kept together in readable multi-line cells. Microsoft Excel is not required for import or export.

## Local data and credentials

Application data is saved under:

`%LOCALAPPDATA%\AVMatrixStudio`

The app keeps `av-matrix-data.backup.json` before replacing the local JSON. Shared-sync recovery files are stored in the `SharedSyncBackups` subfolder.

Equipment username/password fields remain in the local JSON and may be included in Excel and `.avmatrix` exports. Use password-protected `.avmatrix` files and restrict access to exported workbooks when those fields contain sensitive information.

## Requirements

- Windows 10 or Windows 11, 64-bit
- .NET 8 SDK only for building from source
- An active IPv4 adapter for verification
- ICMP allowed for green reachability results
- Google Cloud OAuth Desktop client JSON for direct Google Drive online sync
