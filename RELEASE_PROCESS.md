# InNasc release process

1. Keep development changes on a non-`main` branch until Windows CI is green.
2. Confirm `InNasc.csproj` and `InNasc.GlobalAdmin.csproj` contain the same intended semantic version.
3. Run both smoke suites and clean self-contained Windows publishes.
4. Verify `InNasc.exe` reports product name `InNasc`, and `InNasc.GlobalAdmin.exe` reports `InNasc Global Admin`.
5. Generate and verify SHA-256 files for both executables.
6. Test the branch artifact before requesting a merge to `main`.
7. Only after explicit approval, merge and create a matching `v<version>` tag or an approved `Release InNasc <version>` commit on `main`.

Tags and release-triggering commits publish both executables and their checksum files. Ordinary development-branch builds upload test artifacts only and never create a GitHub Release.
