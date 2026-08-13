# InNasc GitHub integration

The `IamN8Wright/AVMatrix` repository remains the source and update origin during the InNasc 5.0 migration. Product-facing names, executable assets, and CI artifacts use `InNasc`.

Private company-storage repositories use:

- `companies/<company-id>/master.nasc`
- `companies/<company-id>/clients/<client-id>.nascclient`
- `companies/<company-id>/company.json`

Existing `master.avmatrix` and `.avclient` paths remain readable as legacy fallbacks. New writes use `.nasc` and `.nascclient`.

Access tokens are stored in Windows Credential Manager under the InNasc credential target. The app automatically reads and migrates the older AV Matrix credential target when present.
