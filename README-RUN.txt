InNasc 5.1 — Windows x64

GLOBAL ADMIN SETUP
1. Start InNasc.GlobalAdmin.exe.
2. Create or choose the encrypted .nascglobal catalog.
3. Sign in with a Global Admin account.
4. Create a company to generate its .nasc file.
5. Create users, assign company access and roles, then publish access.
6. For accounts created in InNasc 5.0.x, reset the password once before publishing.

USER START
1. Start InNasc.exe.
2. Choose the .nasc file supplied by the Global Admin.
3. Enter the company username and password.
4. Populate and maintain the company workspace according to the assigned role.

ACCOUNT ADMINISTRATION
Company files and account changes are created only in InNasc Global Admin. The user
application cannot create company files or change the authoritative user list.

LEGACY MIGRATION
Use Global Admin > Migrate .avmatrix… to generate a new .nasc company from an older
AV Matrix file. The original .avmatrix file remains unchanged. Existing .avclient
payloads migrate to .nascclient when present.

COMPANY DATA
Keep a company's <company-name>.clients folder with its .nasc file when copying or
backing it up.

SYNC
Company files can use a local/network share or Google Drive. Follow the prompts in
InNasc to pull, check out a client, check it back in, and resolve changes.

LOCAL DATA
Local settings and recovery files are stored in:
%LOCALAPPDATA%\InNasc

WEBSITE
https://InNasc.com
