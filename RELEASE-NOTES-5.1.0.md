# InNasc 5.1.0

- Splits InNasc Global Admin into its own Windows application.
- Makes Global Admin the sole generator of company `.nasc` files and the authority for users, passwords, memberships, and roles.
- Makes the main InNasc application open and authenticate directly against a selected company `.nasc` file.
- Publishes password verifiers and individually wrapped company keys without storing readable passwords.
- Removes company creation and user administration from the main user application.
- Preserves `.avmatrix` and `.avclient` migration in Global Admin.
- Builds, verifies, checksums, and packages both Windows executables in CI.
