# InNasc 5.3.4

- The main synchronization button offers **Check in & push** when this PC has an active checkout, instead of disabling Merge & push without an available main action.
- Check-in merges the checked-out client's metadata with the current company revision. Remote rooms/devices and local edits survive together; overlapping changes require a decision.
- An unchanged checkout can refresh missing rooms during login even when its baseline file is absent. Ambiguous unfinished work keeps its original ancestor and ownership.
- **Recover missing records…** previews missing locations, rooms and devices for an active checkout. Applying recovery keeps local records, saves an encrypted recovery copy, and restores only records explicitly reviewed. It does not publish until check-in.
- Legacy AV Matrix baseline filenames remain readable, with fingerprint verification.
- The backup dialog's Continue button is wide enough for its label.
- Grid repainting disposes the arrow font after drawing, avoiding an accumulating graphics resource leak.

Recovery can only restore records still present in the current company file. Inventory already absent from the company file requires a previous backup. The real 20-minute connected cloud soak remains a separate runtime validation.
