from pathlib import Path

# Preserve the established SyncFile/SyncCompany meaning: Global Admin is authoritative
# and publishes its current roles/password resets. ReconcileFile is the explicit two-way
# operation that first pulls the current .nasc user roster/access levels, then republishes.
path = "InNascCompanyAccessSyncService.cs"
p = Path(path)
s = p.read_text(encoding="utf-8")
old = '''        var old = data.MasterAccess ?? new MasterAccessControl();

        PullCurrentUsers(company, old);

        var next = InNascGlobalCoreService.BuildCompanyAccess('''
new = '''        var old = data.MasterAccess ?? new MasterAccessControl();

        var next = InNascGlobalCoreService.BuildCompanyAccess('''
if old not in s:
    raise SystemExit("Missing pull-in-SyncFile anchor")
s = s.replace(old, new, 1)

anchor = '''    private static void PullCurrentUsers(
        InNascCompanyRecord company,
        MasterAccessControl access)
    {'''
method = '''    public static void ReconcileFile(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company,
        InNascCompanyFileRecord file)
    {
        if (!globalSession.IsGlobalAdmin)
            throw new MasterAuthorizationException("Global Admin access is required.");
        if (!File.Exists(file.FilePath)) return;

        var imported = PortableDataService.ImportBytes(
            File.ReadAllBytes(file.FilePath), file.CompanyKeyBase64);
        PullCurrentUsers(company, imported.Data.MasterAccess ?? new MasterAccessControl());
        InNascGlobalCoreService.Save(globalPath, catalog, globalSession);
        SyncFile(globalPath, catalog, globalSession, company, file);
    }

    public static void ReconcileCompany(
        string globalPath,
        InNascGlobalCatalog catalog,
        InNascGlobalSession globalSession,
        InNascCompanyRecord company)
    {
        foreach (var file in company.Files.Where(file => file.Enabled && File.Exists(file.FilePath)))
            ReconcileFile(globalPath, catalog, globalSession, company, file);
    }

'''
if anchor not in s:
    raise SystemExit("Missing PullCurrentUsers method anchor")
s = s.replace(anchor, method + anchor, 1)
p.write_text(s, encoding="utf-8")

# In the Global Admin UI, only explicit reconcile operations pull from the .nasc.
p = Path("InNascGlobalAdminForm.cs")
s = p.read_text(encoding="utf-8")

# Same-file Import / reconcile block is uniquely followed by RefreshAll(linkedHere.Id).
old = '''                InNascCompanyAccessSyncService.SyncFile(
                    _globalPath, _catalog, _session, _company, linkedHere);
                RefreshAll(linkedHere.Id);'''
new = '''                InNascCompanyAccessSyncService.ReconcileFile(
                    _globalPath, _catalog, _session, _company, linkedHere);
                RefreshAll(linkedHere.Id);'''
if old not in s:
    raise SystemExit("Missing same-file reconcile UI anchor")
s = s.replace(old, new, 1)

# New import immediately reconciles imported users/access after setting root recovery.
old = '''            InNascGlobalCoreService.SetRecoveryPassword(
                _globalPath, _catalog, _session, _company.Id, file.Id, recoveryPassword);
            InNascCompanyAccessSyncService.SyncFile(
                _globalPath, _catalog, _session, _company, file);
            RefreshAll(file.Id);'''
new = '''            InNascGlobalCoreService.SetRecoveryPassword(
                _globalPath, _catalog, _session, _company.Id, file.Id, recoveryPassword);
            InNascCompanyAccessSyncService.ReconcileFile(
                _globalPath, _catalog, _session, _company, file);
            RefreshAll(file.Id);'''
if old not in s:
    raise SystemExit("Missing new-import reconcile UI anchor")
s = s.replace(old, new, 1)

# The Sync selected button is user-facing reconciliation. Find it by its distinctive status string.
old = '''            InNascCompanyAccessSyncService.SyncFile(
                _globalPath, _catalog, _session, _company, file);
            _status.Text = $"Reconciled {file.Name}: pulled current .nasc users/access and republished license name, tier, expiration, logo, and recovery access.";'''
new = '''            InNascCompanyAccessSyncService.ReconcileFile(
                _globalPath, _catalog, _session, _company, file);
            _status.Text = $"Reconciled {file.Name}: pulled current .nasc users/access and republished license name, tier, expiration, logo, and recovery access.";'''
if old not in s:
    raise SystemExit("Missing Sync selected reconcile UI anchor")
s = s.replace(old, new, 1)
p.write_text(s, encoding="utf-8")

# The dedicated reconciliation smoke should exercise the pull path; lifecycle/admin-change
# tests continue to use SyncFile/SyncCompany so Global Admin edits remain authoritative.
p = Path("tests/InNasc.GlobalAdmin.SmokeTests/Program.cs")
s = p.read_text(encoding="utf-8")
old = '''        InNascCompanyAccessSyncService.SyncFile(
            globalPath, global.Catalog, global.Session, company, imported);
        var pulled = company.Users.Single(user => user.Username == "field-tech");'''
new = '''        InNascCompanyAccessSyncService.ReconcileFile(
            globalPath, global.Catalog, global.Session, company, imported);
        var pulled = company.Users.Single(user => user.Username == "field-tech");'''
if old not in s:
    raise SystemExit("Missing local-user reconcile smoke anchor")
s = s.replace(old, new, 1)
p.write_text(s, encoding="utf-8")
