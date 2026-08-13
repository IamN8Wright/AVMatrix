# InNasc release process

1. Keep development changes on a non-`main` branch until Windows CI is green.
2. Confirm `InNasc.csproj` contains the intended semantic version.
3. Run the smoke suite and a clean self-contained Windows publish.
4. Verify `InNasc.exe` reports product name `InNasc` and the expected version.
5. Generate `InNasc.exe.sha256` and verify it against the executable.
6. Test the branch artifact before requesting a merge to `main`.
7. Only after explicit approval, merge and create a matching `v<version>` tag or an approved `Release InNasc <version>` commit on `main`.

Tags and release-triggering commits publish `InNasc.exe` and `InNasc.exe.sha256`. Ordinary development-branch builds upload test artifacts only and never create a GitHub Release.
