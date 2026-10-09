# InNasc 5.3.6

- The sidebar sync button opens the connection used by the unfinished checkout or signed-in company. A company-file checkout no longer sends the user to a Google Drive dialog whose actions are disabled.
- Inspecting Google Drive status from a company-file workspace no longer replaces that workspace's account/checkout metadata or writes its local data and sync baseline.
- The welcome screen remembers the last selected connection when both Google Drive and a company-file link are configured. An unfinished checkout continues to use its original connection.

No checkout is cleared or transferred between files by this routing change. Google Drive and a local/file-share copy can contain different revisions; their records must be synchronized through the correct connection. The real 20-minute connected cloud idle/reconnect test remains pending.
