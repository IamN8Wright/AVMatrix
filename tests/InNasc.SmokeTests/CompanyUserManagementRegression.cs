namespace InNasc.SmokeTests;

internal static class CompanyUserManagementRegression
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "InNasc-Users-QC-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = new ClientRecord { Name = "Client A", Locations = [new LocationRecord
                { Rooms = [new RoomRecord { Name = "Original", Equipment = [new EquipmentRecord()] }] }] };
            var second = new ClientRecord { Name = "Client B" };
            var original = new AppData { Clients = [first, second] };
            MasterAccessService.CreateInitialOwner(original.MasterAccess, "admin", "Admin", "Company-admin-pass-537");
            var admin = MasterAccessService.SignIn(original.MasterAccess, "admin", "Company-admin-pass-537");
            Throws<InvalidOperationException>(() => MasterAccessService.UpdateUser(original.MasterAccess,
                admin, admin.UserId, "Admin", MasterUserRole.Tech, true), "The last admin must be retained.");
            var path = Path.Combine(root, "Company.nasc");
            PortableDataService.ExportMaster(path, original, admin);
            var store = new DataStore(Path.Combine(root, "Workstation"));
            var local = new AppData { Settings = new AppSettings { SharedMasterPath = path } };
            SharedSyncService.Pull(local, store, admin.MasterKey, admin);
            SharedSyncService.CheckoutClient(local, store, first.Id, admin, false, admin.MasterKey);
            var token = local.Settings.ActiveCheckoutToken;
            var device = local.Clients[0].Locations[0].Rooms[0].Equipment[0];
            device.Notes = "Pending device edit";
            device.ConfigurationFiles.Add(new DeviceConfigurationFile
                { FileName = "pending.cfg", ContentIncluded = true, ContentBase64 = "AQID" });
            var baselineBytes = File.ReadAllBytes(SyncBaselineStore.SharedPath(store));
            var baselineFingerprint = local.Settings.SharedMasterFingerprint;
            var remote = PortableDataService.Import(path, admin.MasterKey).Data;
            remote.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "Remote addition" });
            PortableDataService.ExportMaster(path, remote, admin);
            var snapshot = SharedSyncService.Inspect(path, admin.MasterKey);
            var updated = MasterAccessService.Clone(snapshot.Contents.Data.MasterAccess);
            var employee = MasterAccessService.AddUser(updated, admin, "employee", "Employee",
                "Employee-pass-537", MasterUserRole.Tech);
            MasterAccessService.UpdateClientAccess(updated, admin, employee.Id, false, [first.Id]);
            var allUser = MasterAccessService.AddUser(updated, admin, "all-user", "All clients user",
                "All-user-pass-537", MasterUserRole.Tech);
            var secondAdmin = MasterAccessService.AddUser(updated, admin, "other-admin", "Other admin",
                "Other-admin-pass-537", MasterUserRole.Owner);
            SharedSyncService.SaveAccessControl(local, store, updated, admin, false, admin.MasterKey,
                expectedFingerprint: snapshot.Fingerprint, preserveInventoryBaseline: true);
            Check(local.Settings.ActiveCheckoutToken == token && device.Notes == "Pending device edit" &&
                device.ConfigurationFiles[0].ContentBase64 == "AQID", "Account saving lost pending work or checkout ownership.");
            Check(local.Settings.SharedMasterFingerprint == baselineFingerprint &&
                File.ReadAllBytes(SyncBaselineStore.SharedPath(store)).SequenceEqual(baselineBytes),
                "Account saving advanced the inventory ancestor and could remove remote rooms on check-in.");
            var saved = SharedSyncService.Inspect(path, admin.MasterKey);
            var access = saved.Contents.Data.MasterAccess;
            var restrictedSession = MasterAccessService.SignIn(access, "employee", "Employee-pass-537");
            Check(MasterAccessService.CanAccessClient(access, restrictedSession, first.Id) &&
                !MasterAccessService.CanAccessClient(access, restrictedSession, second.Id), "Selected-client access was not published.");
            Throws<MasterAuthorizationException>(() => SharedSyncService.CheckoutClient(
                new AppData { Settings = new AppSettings { SharedMasterPath = path } }, store,
                second.Id, restrictedSession, false, restrictedSession.MasterKey), "A restricted user checked out an unassigned client.");
            var allSession = MasterAccessService.SignIn(access, allUser.Username, "All-user-pass-537");
            var adminSession = MasterAccessService.SignIn(access, secondAdmin.Username, "Other-admin-pass-537");
            Check(MasterAccessService.CanAccessClient(access, allSession, Guid.NewGuid()) &&
                MasterAccessService.CanAccessClient(access, adminSession, Guid.NewGuid()), "All-client access must include future clients.");
            Throws<MasterAuthorizationException>(() => MasterAccessService.AddUser(access, restrictedSession,
                "illicit", "Illicit", "Illicit-pass-537", MasterUserRole.Owner), "An employee managed company users.");
            var forged = MasterAccessService.Clone(access);
            forged.Users.Single(user => user.Id == restrictedSession.UserId).Role = MasterUserRole.Owner;
            var beforeDeniedSave = File.ReadAllBytes(path);
            Throws<MasterAuthorizationException>(() => SharedSyncService.SaveAccessControl(local, store,
                forged, restrictedSession, false, restrictedSession.MasterKey, saved.Fingerprint, true),
                "An employee published a forged admin profile.");
            Check(File.ReadAllBytes(path).SequenceEqual(beforeDeniedSave), "A denied account save modified the company file.");
            SharedSyncService.CheckInClient(local, store, admin, admin.MasterKey);
            Check(local.Clients[0].Locations[0].Rooms.Any(room => room.Name == "Remote addition"),
                "Checking in after managing users erased a remotely added room.");
            var package = ClientSubmatrixService.ReadClientPackage(File.ReadAllBytes(
                ClientSubmatrixService.SharedClientPath(path, first.Id)), first.Id, admin.MasterKey);
            Check(package.Locations[0].Rooms[0].Equipment[0].Notes == "Pending device edit" &&
                package.Locations[0].Rooms[0].Equipment[0].ConfigurationFiles[0].ContentBase64 == "AQID",
                "Check-in after managing users lost the pending edit or configuration bytes.");
            var beforeEditor = SharedSyncService.Inspect(path, admin.MasterKey);
            var changed = beforeEditor.Contents.Data;
            changed.ProjectName = "Changed while user editor was open";
            PortableDataService.ExportMaster(path, changed, admin);
            var concurrentBytes = File.ReadAllBytes(path);
            Throws<SharedMasterConflictException>(() => SharedSyncService.SaveAccessControl(local, store,
                beforeEditor.Contents.Data.MasterAccess, admin, false, admin.MasterKey, beforeEditor.Fingerprint, true),
                "A stale user editor overwrote a newer company revision.");
            Check(File.ReadAllBytes(path).SequenceEqual(concurrentBytes), "A stale editor changed the company file.");
            var revoked = SharedSyncService.Inspect(path, admin.MasterKey).Contents.Data;
            var staleProposal = MasterAccessService.Clone(revoked.MasterAccess);
            MasterAccessService.UpdateUser(revoked.MasterAccess, adminSession, admin.UserId, "Former admin", MasterUserRole.Tech, true);
            PortableDataService.ExportMaster(path, revoked, adminSession);
            var revokedSnapshot = SharedSyncService.Inspect(path, adminSession.MasterKey);
            Throws<MasterAuthorizationException>(() => SharedSyncService.SaveAccessControl(local, store,
                staleProposal, admin, false, admin.MasterKey, revokedSnapshot.Fingerprint, true),
                "A revoked admin used an old session to restore its privileges.");
            Console.WriteLine("Company user QC passed: published logins, selected/all/future clients, admin-only management, last admin, stale/revoked saves, pending work/config bytes, checkout ownership and post-account-save merge.");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Company user QC: " + message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Company user QC: " + message);
    }
}
