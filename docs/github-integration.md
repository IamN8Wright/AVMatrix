# GitHub Integration

AV Matrix Studio will use GitHub for source control and release distribution. Customer Master Matrix data is stored separately in the private `IamN8Wright/AVMatrix_MasterMatrixStorage` repository.

## Application updates

The application should query the latest GitHub Release, compare semantic versions, download the matching Windows release asset, verify its SHA-256 checksum, and then hand off installation to a separate updater process so the running executable can be safely replaced.

## Master Matrix transport

GitHub is an optional remote provider alongside Google Drive and local/network storage. Each company is isolated under its own folder. The application should use the remote Git revision as the concurrency baseline and invoke the existing merge workflow if the remote revision changes before check-in.
