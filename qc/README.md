# InNasc quality control

Run `Run-QC.cmd` on Windows. It restores branding, runs the inventory tests, Windows user-app/UI tests and Global Admin tests, then starts a separate Codex review in a read-only session. A failing or incomplete review returns a failing exit code. Test output and the JSON review live in `artifacts/qc/`.

The LLM runner requires the Codex CLI to be installed and signed in (`codex login`). It reuses the CLI's saved authentication; no credentials are committed. CLI setup and non-interactive flags: https://learn.chatgpt.com/docs/non-interactive-mode . The model review cannot guarantee that the app is bug-free. Automated assertions decide regressions; the model traces risks and flags missing validation.

For tests only, run `Run-QC.cmd -TestsOnly`. GitHub runs portable inventory QC on pushes and pull requests, and the Windows build workflow runs all three test suites. The local LLM review is not an unattended GitHub model service; its authentication/setup must be available on the PC running it.

## Required release cases

- Sync controls: disconnected/missing-link/busy/setup-only states must look unavailable and state the reason. Connected controls must restore their enabled style, support keyboard focus, and have unobstructed click targets. Checkout controls appear only for the matching backend. Literal ampersands must be visible in action labels.
- Login against fresh master data: Providence/CSC shows all three rooms without a manual Pull.
- Resume an owned checkout: receive remote added rooms and other clients; retain unpushed edits, deletions, configuration-file bytes and the ownership token. If there is no valid merge ancestor, retain the checkout and do not guess how to reconcile deleted rooms.
- Legacy baseline filenames: read both SharedMasterBaseline.avmatrix and GoogleDriveMasterBaseline.avmatrix when the new filename is absent; require the expected byte fingerprint.
- Check-in from a stale client: combine remote room/device additions with local edits and configuration payloads. Preserve known intentional deletions, ask about overlapping fields, and reject an ambiguous missing ancestor before publishing. The main action must offer Check in & push for this backend's checkout.
- Missing-record recovery: preview exact additions, retain existing local records/fields/configuration bytes and checkout ownership, save the previous inventory, and reject stale previews or lost ownership. The recovered records must survive subsequent login and check-in. Never label an inventory recovery preview as automatic deletion reconciliation.
- Plain push and merged push: refresh tree/grid/current client after either result. Preserve selected client/location/room IDs; show the parent client path and device counts.
- Counts: add three records to 169 and check the company total is 172 in the footer, license usage and merge preview. Room/filter totals must be labeled separately. Interface detail rows do not count as devices.
- Editing during a cloud upload: after serialization, add/edit/delete inventory; verify upload completion retains that later work and marks it pending for the next push.
- Import 45 same-model devices: retain all 45. Do not merge different serial numbers or reused hostnames/private IPs across locations. Reject a changed import destination or inventory after preview, with no partial mutation.
- Checkout takeover: the old PC preserves unfinished work, loses write authority and shows the new holder.
- Real Windows/cloud soak: leave the application connected for at least 20 minutes; navigate rooms, use filters, edit a device, push and check counts throughout. Repeat with an interrupted connection and reconnection. Verify responsiveness and no silent data loss. Record the actual elapsed duration and backend; accelerated tests are not this check.

The tests repeat 240 graph replacements (equivalent to the number of five-second ticks in 20 minutes), without sleeping or using real customer services. Windows UI tests additionally check the visible labels and selected tree node. Real cloud authentication, wall-clock idle behavior and connectivity require the live soak above.
