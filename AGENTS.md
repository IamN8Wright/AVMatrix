# InNasc development and QC

After changing this app, perform a distinct QC review using qc/review-prompt.txt and qc/README.md. Trace real user workflows and object replacements across awaits; do not approve from a code summary alone. Run meaningful regression tests and report the exact checks that ran. Never describe accelerated timer tests as a real elapsed cloud soak.

On Windows run Run-QC.ps1. On other platforms run tests/InNasc.CoreTests and disclose that Windows UI/Global Admin validation is pending unless the GitHub Windows job passed. Inspect CI results before claiming a testable build is ready. Preserve customer data, account access, checkout ownership and configuration-file bytes. Resolve selected entities by stable IDs after sync. Keep company totals distinct from view/filter totals.

Do not publish a release just because QC passes. Keep changes reviewable and state any runtime verification still needed.
