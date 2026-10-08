# InNasc 5.3.3 — synchronization and QC fixes

- Refresh the inventory after a plain push as well as a merge; rebind selected containers by their stable IDs so the equipment view does not lose its parent client or appear empty.
- Preserve location/room and device selection during live updates. Reuse grid/scope fonts during repeated refreshes rather than allocating a font per device on every refresh.
- Update footer company totals with inventory changes and label company totals separately from filtered/location/room totals.
- When resuming an owned checkout at login, refresh master inventory against the last downloaded ancestor while retaining unpushed local work, configuration bytes and ownership. Refresh other clients even when no ancestor is available; preserve the unfinished checked-out client in that fallback.
- Preserve additions, edits and deletions made while a cloud push awaits network responses, and leave them pending for the next push.
- Keep same-model import rows as separate devices without an identity match; prevent conflicting serial/equipment identities and cross-location network identities from merging. Reject stale import previews without partial mutation.
- Add portable inventory regressions, Windows UI refresh regressions, and a repeatable local LLM QC runner.

Validation: portable inventory QC passed locally, including 169→172, three-room checkout resume, late-upload edits, import identity, preview races and 240 accelerated sync cycles. The GitHub Windows workflow provides full UI/build validation. A real 20-minute Windows/cloud idle soak remains required before claiming the reported elapsed-time behavior is fully verified.
