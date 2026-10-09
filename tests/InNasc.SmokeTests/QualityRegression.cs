using System.Text.Json;

namespace InNasc.SmokeTests;

internal static class QualityRegression
{
    public static void Run()
    {
        var baseline = Inventory(169);
        var staleClient = baseline.Clients[0];
        var staleLocation = staleClient.Locations[0];
        var fresh = Clone(baseline);
        fresh.Clients[0].Locations[0].Rooms[0].Equipment.AddRange(Devices(3));
        var contexts = Contexts(fresh);
        Assert(WorkspaceInventory.Scope(contexts, staleLocation).Count() == 172,
            "A replaced location must still resolve to all 172 current devices.");
        Assert(ReferenceEquals(WorkspaceInventory.Resolve(fresh, staleLocation),
            fresh.Clients[0].Locations[0]), "Selection must rebind to the current inventory instance.");
        Assert(WorkspaceInventory.CountSummary(20, 169, 172).Contains("Company total: 172"),
            "Filtered counts must distinguish the company total.");
        Assert(DeviceLimitPolicy.UsageText(fresh.MasterAccess, fresh).Contains("172"),
            "License count must include the new devices.");
        var removed = Clone(fresh);
        removed.Clients[0].Locations.Clear();
        Assert(WorkspaceInventory.Resolve(removed, staleLocation) is null &&
            !WorkspaceInventory.Scope(Contexts(removed), staleLocation).Any(),
            "A removed scope must not display unrelated equipment.");

        var local = Clone(baseline);
        var beforeUpload = SyncPublishApplyService.Capture(local);
        var published = Clone(beforeUpload);
        local.Clients[0].Locations[0].Rooms[0].Equipment.AddRange(Devices(3));
        local.Clients[0].Name = "Edited during upload";
        published.Clients.Add(new ClientRecord { Name = "Other technician's client" });
        SyncPublishApplyService.Apply(local, beforeUpload, published);
        Assert(DeviceLimitPolicy.CountDevices(local) == 172 && local.Clients.Count == 2,
            "Upload completion must preserve later local additions and independent remote work.");
        Assert(local.Clients[0].Name == "Edited during upload", "A later edit was discarded.");
        Assert(local.Settings.GoogleDriveLocalContentFingerprint != SyncContentFingerprint.Compute(local),
            "Unsaved late edits must remain pending for the next push.");
        var deleting = Clone(beforeUpload);
        var removedDeviceId = deleting.Clients[0].Locations[0].Rooms[0].Equipment[0].Id;
        deleting.Clients[0].Locations[0].Rooms[0].Equipment.RemoveAt(0);
        SyncPublishApplyService.Apply(deleting, beforeUpload, published);
        Assert(DeviceLimitPolicy.CountDevices(deleting) == 168 &&
            !Contexts(deleting).Any(item => item.Equipment.Id == removedDeviceId),
            "A deletion made during upload must remain deleted locally.");
        var clean = Clone(published);
        SyncPublishApplyService.Apply(clean, Clone(published), published);
        Assert(clean.Settings.GoogleDriveLocalContentFingerprint == SyncContentFingerprint.Compute(clean),
            "A completed upload without later edits must report synchronized.");

        var resumeBaseline = Inventory(1);
        var resumed = Clone(resumeBaseline);
        resumed.Settings.ActiveCheckoutClientId = resumed.Clients[0].Id;
        resumed.Settings.ActiveCheckoutToken = Guid.NewGuid();
        var token = resumed.Settings.ActiveCheckoutToken;
        var firstDevice = resumed.Clients[0].Locations[0].Rooms[0].Equipment[0];
        firstDevice.Notes = "Unpushed local work";
        firstDevice.ConfigurationFiles.Add(new DeviceConfigurationFile
        {
            FileName = "offline.cfg", ContentIncluded = true, ContentBase64 = "AQID"
        });
        var company = Clone(resumeBaseline);
        company.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "23Hr Meeting Room" });
        company.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "Large Huddle Room" });
        company.Clients.Add(new ClientRecord { Name = "New remote client" });
        CheckoutResumeService.RefreshInventory(resumed, company, resumeBaseline);
        Assert(resumed.Clients[0].Locations[0].Rooms.Count == 3 && resumed.Clients.Count == 2,
            "Resuming a checkout must show all three rooms and newly published clients.");
        var recovered = resumed.Clients[0].Locations[0].Rooms[0].Equipment[0];
        Assert(recovered.Notes == "Unpushed local work" &&
            recovered.ConfigurationFiles[0].ContentBase64 == "AQID" &&
            resumed.Settings.ActiveCheckoutToken == token,
            "Login refresh must preserve local edits, configuration bytes and ownership.");
        // Repeated timer refreshes simulate 20 minutes of five-second sync cycles.
        for (var cycle = 0; cycle < 240; cycle++)
        {
            var selected = resumed.Clients[0].Locations[0];
            CheckoutResumeService.RefreshInventory(resumed, company, company);
            Assert(WorkspaceInventory.Scope(Contexts(resumed), selected).Count() == 1,
                "Repeated sync detached the selected location.");
            Assert(resumed.Clients[0].Locations[0].Rooms.Count == 3,
                "Repeated sync lost a room.");
        }
        var deletingCheckout = Clone(resumed);
        deletingCheckout.Clients[0].Locations[0].Rooms.RemoveAll(room => room.Name == "23Hr Meeting Room");
        var remoteWithAddition = Clone(company);
        remoteWithAddition.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "New remote room" });
        CheckoutResumeService.RefreshInventory(deletingCheckout, remoteWithAddition, company);
        Assert(!deletingCheckout.Clients[0].Locations[0].Rooms.Any(room => room.Name == "23Hr Meeting Room") &&
            deletingCheckout.Clients[0].Locations[0].Rooms.Any(room => room.Name == "New remote room"),
            "Checkout resume must retain a local deletion and an independent remote addition.");
        var noAncestor = Clone(resumed);
        CheckoutResumeService.RefreshInventory(noAncestor, company, null);
        Assert(noAncestor.Clients[0].Locations[0].Rooms[0].Equipment[0].Notes == "Unpushed local work",
            "A missing ancestor must not erase unfinished checkout work.");

        RunImportRegression();
        RunCheckoutRecoveryRegression();
        RunSyncNavigationRegression();
        Console.WriteLine("QC passed: scope rebinding, 169/172 counts, late upload edits, checkout resume, 240 sync cycles, import identity and preview races.");
    }

    private static void RunSyncNavigationRegression()
    {
        var settings = new AppSettings
        {
            SharedMasterPath = "company.nasc",
            GoogleDriveFileId = "cloud-company",
            LastMasterTarget = nameof(SyncTarget.GoogleDrive)
        };
        Assert(SyncNavigationPolicy.ForWorkspace(settings) == SyncTarget.GoogleDrive,
            "Welcome must restore the last Google workspace even with an old company-file link.");
        Assert(SyncNavigationPolicy.ForWorkspace(settings, SyncTarget.SharedFile) == SyncTarget.SharedFile,
            "Sync must use the signed-in company rather than an unrelated cached Google link.");
        settings.ActiveCheckoutClientId = Guid.NewGuid();
        settings.ActiveCheckoutTarget = nameof(SyncTarget.SharedFile);
        Assert(SyncNavigationPolicy.ForWorkspace(settings, SyncTarget.GoogleDrive) == SyncTarget.SharedFile,
            "An unfinished company-file checkout must route to its original backend.");
        Assert(!SyncNavigationPolicy.CanApplyStatus(settings, SyncTarget.GoogleDrive, SyncTarget.SharedFile),
            "Inspecting Google must not replace a file workspace's checkout metadata.");
        settings.ActiveCheckoutTarget = nameof(SyncTarget.GoogleDrive);
        Assert(SyncNavigationPolicy.ForWorkspace(settings) == SyncTarget.GoogleDrive &&
            !SyncNavigationPolicy.CanApplyStatus(settings, SyncTarget.SharedFile, SyncTarget.GoogleDrive),
            "Google checkout state must remain associated with the Google workspace.");
        settings.ActiveCheckoutClientId = null;
        settings.GoogleDriveFileId = string.Empty;
        Assert(SyncNavigationPolicy.ForWorkspace(settings) == SyncTarget.SharedFile,
            "A remembered backend without a link must fall back to an available connection.");
        Assert(!SyncNavigationPolicy.CanApplyStatus(settings, SyncTarget.GoogleDrive, SyncTarget.SharedFile),
            "Cross-backend inspection must remain read-only even without a checkout.");
        Console.WriteLine("Sync routing QC passed: checkout backend, signed-in workspace, last selected company and read-only status inspection.");
    }

    private static void RunCheckoutRecoveryRegression()
    {
        var root = Path.Combine(Path.GetTempPath(), "InNasc-Checkout-QC-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DataStore(Path.Combine(root, "Workstation"));
            var before = Inventory(1);
            var access = before.MasterAccess;
            MasterAccessService.CreateInitialOwner(access, "qc-owner", "QC", "QC-Checkout-password-534");
            var session = MasterAccessService.SignIn(access, "qc-owner", "QC-Checkout-password-534");
            var path = Path.Combine(root, "Company.nasc");
            PortableDataService.ExportMaster(path, before, session);
            var local = new AppData();
            local.Settings.SharedMasterPath = path;
            SharedSyncService.Pull(local, store, session.MasterKey, session);
            var id = before.Clients[0].Id;
            SharedSyncService.CheckoutClient(local, store, id, session, false, session.MasterKey);
            var token = local.Settings.ActiveCheckoutToken;
            local.Clients[0].Locations[0].Rooms[0].Equipment[0].Notes = "Keep local edit";
            local.Clients[0].Locations[0].Rooms[0].Equipment[0].ConfigurationFiles.Add(
                new DeviceConfigurationFile { FileName = "local.cfg", ContentIncluded = true, ContentBase64 = "AQID" });
            var remote = PortableDataService.Import(path, session.MasterKey).Data;
            remote.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "Remote added room", Equipment = Devices(2) });
            PortableDataService.ExportMaster(path, remote, session);
            SharedSyncService.CheckInClient(local, store, session, session.MasterKey);
            Assert(local.Clients[0].Locations[0].Rooms.Count == 2 && DeviceLimitPolicy.CountDevices(local) == 3,
                "Check-in must retain remote rooms and devices, rather than replacing them with the stale local client.");
            var package = ClientSubmatrixService.ReadClientPackage(
                File.ReadAllBytes(ClientSubmatrixService.SharedClientPath(path, id)), id, session.MasterKey);
            Assert(package.Locations[0].Rooms[0].Equipment[0].Notes == "Keep local edit" &&
                package.Locations[0].Rooms[0].Equipment[0].ConfigurationFiles[0].ContentBase64 == "AQID",
                "Check-in lost local edits or configuration payloads.");
            Assert(!local.Settings.ActiveCheckoutClientId.HasValue,
                "Successful check-in must release local checkout ownership.");

            SharedSyncService.CheckoutClient(local, store, id, session, false, session.MasterKey);
            local.Clients[0].Locations[0].Rooms[0].Equipment[0].Notes = "Second local edit";
            var conflicting = PortableDataService.Import(path, session.MasterKey).Data;
            conflicting.Clients[0].Locations[0].Rooms[0].Equipment[0].Notes = "Remote conflicting edit";
            PortableDataService.ExportMaster(path, conflicting, session);
            var beforeConflict = File.ReadAllBytes(path);
            var asked = false;
            try { SharedSyncService.CheckInClient(local, store, session, session.MasterKey); }
            catch (MergeResolutionRequiredException) { asked = true; }
            Assert(asked && File.ReadAllBytes(path).SequenceEqual(beforeConflict) &&
                local.Settings.ActiveCheckoutClientId == id,
                "Overlapping check-in fields must require a decision without changing the master or releasing checkout.");
            SharedSyncService.CheckInClient(local, store, session, session.MasterKey, MergeConflictPreference.ThisPc);
            Assert(local.Clients[0].Locations[0].Rooms[0].Equipment[0].Notes == "Second local edit",
                "Explicit check-in resolution did not preserve the chosen local value.");

            // AV Matrix installations may still store the baseline under its old name.
            foreach (var target in new[] { SyncTarget.SharedFile, SyncTarget.GoogleDrive })
            {
                var bytes = File.ReadAllBytes(path);
                SyncBaselineStore.Save(store, target, bytes);
                var currentPath = target == SyncTarget.SharedFile
                    ? SyncBaselineStore.SharedPath(store) : SyncBaselineStore.GoogleDrivePath(store);
                var legacyPath = Path.Combine(store.DataDirectory, target == SyncTarget.SharedFile
                    ? "SharedMasterBaseline.avmatrix" : "GoogleDriveMasterBaseline.avmatrix");
                File.Move(currentPath, legacyPath);
                Assert(SyncBaselineStore.Load(store, target, SyncBaselineStore.Fingerprint(bytes), session.MasterKey)
                    .Clients[0].Locations[0].Rooms.Count == 2, "Legacy baseline filenames must remain readable.");
                var rejected = false;
                try { SyncBaselineStore.Load(store, target, "wrong-fingerprint", session.MasterKey); }
                catch (SharedMasterConflictException) { rejected = true; }
                Assert(rejected, "A legacy baseline still must match the exact expected fingerprint.");
                SyncBaselineStore.Delete(store, target);
            }

            var noAncestor = Inventory(1);
            var expanded = Clone(noAncestor);
            expanded.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "Missing room", Equipment = Devices(2) });
            noAncestor.Settings.ActiveCheckoutClientId = noAncestor.Clients[0].Id;
            noAncestor.Settings.ActiveCheckoutToken = token;
            noAncestor.Settings.ActiveCheckoutTarget = nameof(SyncTarget.SharedFile);
            noAncestor.Settings.ActiveCheckoutBaselineFingerprint = SyncContentFingerprint.ComputeClient(noAncestor.Clients[0]);
            Assert(CheckoutResumeService.RefreshInventory(noAncestor, expanded, null) &&
                noAncestor.Clients[0].Locations[0].Rooms.Count == 2,
                "An unchanged checkout must receive missing rooms even without a baseline file.");

            var edited = Inventory(1);
            edited.Settings.ActiveCheckoutClientId = edited.Clients[0].Id;
            edited.Settings.ActiveCheckoutToken = token;
            edited.Settings.ActiveCheckoutTarget = nameof(SyncTarget.SharedFile);
            edited.Settings.ActiveCheckoutBaselineFingerprint = SyncContentFingerprint.ComputeClient(edited.Clients[0]);
            var editedRemote = Clone(edited);
            editedRemote.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "Recover me", Equipment = Devices(2) });
            edited.Clients[0].Locations[0].Rooms[0].Equipment[0].Notes = "Unfinished local work";
            var previousCheckoutFingerprint = edited.Settings.ActiveCheckoutBaselineFingerprint;
            Assert(!CheckoutResumeService.RefreshInventory(edited, editedRemote, null) &&
                edited.Settings.ActiveCheckoutBaselineFingerprint == previousCheckoutFingerprint,
                "Ambiguous checkout refresh must not silently adopt an ancestor and imply deletions.");
            var blocked = false;
            try { CheckoutInventoryService.MergeForCheckIn(edited.Clients[0], editedRemote.Clients[0], null,
                edited.Settings.ActiveCheckoutBaselineFingerprint); }
            catch (SharedMasterConflictException) { blocked = true; }
            Assert(blocked, "Checking in an ambiguous incomplete checkout must preserve both sides.");
            var recovery = CheckoutInventoryService.PrepareRecovery(edited.Clients[0], editedRemote.Clients[0]);
            Assert(recovery.AddedRecords.Count == 3 && recovery.Client.Locations[0].Rooms.Count == 2,
                "Recovery must preview the missing room and its two devices.");
            Assert(recovery.Client.Locations[0].Rooms[0].Equipment[0].Notes == "Unfinished local work" &&
                edited.Clients[0].Locations[0].Rooms.Count == 1,
                "Recovery preview must keep edits and leave live inventory untouched.");
            var bytesForRecovery = PortableDataService.ExportBytes(editedRemote, session.MasterKey, out _);
            var fingerprint = CheckoutInventoryService.LocalFingerprint(edited);
            var stale = Clone(edited);
            stale.Clients[0].Locations[0].Rooms[0].Equipment[0].ConfigurationFiles.Add(
                new DeviceConfigurationFile { FileName = "late.cfg", ContentIncluded = true, ContentBase64 = "BAUG" });
            var rejectedPreview = false;
            try { CheckoutInventoryService.ApplyRecovery(stale, editedRemote, store, SyncTarget.SharedFile,
                bytesForRecovery, recovery, fingerprint, session.MasterKey); }
            catch (InvalidOperationException) { rejectedPreview = true; }
            Assert(rejectedPreview && stale.Clients[0].Locations[0].Rooms.Count == 1 &&
                stale.Settings.ActiveCheckoutToken == token,
                "A changed configuration payload must invalidate recovery before any inventory or ownership mutation.");
            var ownershipRemote = Clone(editedRemote);
            ownershipRemote.MasterAccess = MasterAccessService.Clone(access);
            ownershipRemote.MasterAccess.Checkouts.Add(new ClientCheckoutRecord
            {
                ClientId = edited.Clients[0].Id, UserId = session.UserId, CheckoutToken = token!.Value
            });
            CheckoutInventoryService.RequireOwnership(edited, ownershipRemote, session, SyncTarget.SharedFile);
            ownershipRemote.MasterAccess.Checkouts[0].CheckoutToken = Guid.NewGuid();
            var rejectedOwner = false;
            try { CheckoutInventoryService.RequireOwnership(edited, ownershipRemote, session, SyncTarget.SharedFile); }
            catch (InvalidOperationException) { rejectedOwner = true; }
            Assert(rejectedOwner, "Recovery must reject a checkout taken over by another PC.");
            CheckoutInventoryService.ApplyRecovery(edited, editedRemote, store, SyncTarget.SharedFile,
                bytesForRecovery, recovery, fingerprint, session.MasterKey);
            Assert(edited.Settings.ActiveCheckoutToken == token && DeviceLimitPolicy.CountDevices(edited) == 3,
                "Recovery must retain checkout ownership and all recovered devices.");
            var merged = CheckoutInventoryService.MergeForCheckIn(edited.Clients[0], editedRemote.Clients[0],
                SyncBaselineStore.Load(store, SyncTarget.SharedFile, edited.Settings.SharedMasterFingerprint, session.MasterKey)
                    .Clients[0], edited.Settings.ActiveCheckoutBaselineFingerprint);
            Assert(merged.Locations[0].Rooms.Count == 2 &&
                merged.Locations[0].Rooms[0].Equipment[0].Notes == "Unfinished local work",
                "Recovered records and local edits must survive the next check-in merge.");
            var backup = Directory.GetFiles(Path.Combine(store.DataDirectory, "CheckoutRecoveryBackups"), "*.nasc").Single();
            Assert(PortableDataService.Import(backup, session.MasterKey).Data.Clients[0].Locations[0].Rooms.Count == 1,
                "Recovery must save the exact prior inventory in an encrypted backup.");

            var deletionBaseline = Clone(editedRemote);
            var withDeletion = Clone(deletionBaseline);
            withDeletion.Clients[0].Locations[0].Rooms.RemoveAt(1);
            var newer = Clone(deletionBaseline);
            newer.Clients[0].Locations[0].Rooms.Add(new RoomRecord { Name = "Independent addition" });
            var deletedMerge = CheckoutInventoryService.MergeForCheckIn(withDeletion.Clients[0], newer.Clients[0],
                deletionBaseline.Clients[0], "");
            Assert(deletedMerge.Locations[0].Rooms.All(room => room.Name != "Recover me") &&
                deletedMerge.Locations[0].Rooms.Any(room => room.Name == "Independent addition"),
                "A known local deletion and independent remote addition must both survive check-in.");
            Console.WriteLine("Checkout QC passed: shared-file check-in, remote additions, config bytes, legacy baselines, missing-ancestor login, explicit recovery and intentional deletions.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void RunImportRegression()
    {
        var data = Inventory(0);
        var client = data.Clients[0];
        var location = client.Locations[0];
        var rows = Enumerable.Range(0, 45).Select(i => new ImportedEquipment("Conference",
            new EquipmentRecord { Description = "Speaker", Manufacturer = "Example", PartNumber = "SameModel" })).ToList();
        var plan = ExcelImportMergeService.Analyze(client, location, null, rows);
        Assert(plan.AddedDevices == 45, "45 identical models must remain 45 separate devices.");
        ExcelImportMergeService.Apply(client, location, null, plan);
        Assert(DeviceLimitPolicy.CountDevices(data) == 45, "Import lost physical devices.");
        location = client.Locations.Single(item => item.Id == location.Id);
        var existing = location.Rooms.SelectMany(room => room.Equipment).First();
        existing.SerialNumber = "A";
        existing.Hostname = "speaker";
        var distinct = new ImportedEquipment("Conference", new EquipmentRecord
            { Description = "Speaker", Hostname = "speaker", SerialNumber = "B" });
        Assert(ExcelImportMergeService.Analyze(client, location, null, [distinct]).AddedDevices == 1,
            "A conflicting serial number must prevent automatic merging.");
        var otherLocation = new LocationRecord { Rooms = [new RoomRecord()] };
        client.Locations.Add(otherLocation);
        var repeatedHost = new ImportedEquipment("Conference", new EquipmentRecord { Hostname = "speaker" });
        Assert(ExcelImportMergeService.Analyze(client, otherLocation, null, [repeatedHost]).AddedDevices == 1,
            "A reused hostname at another location must not merge devices.");
        var same = new ImportedEquipment("Conference", new EquipmentRecord
            { SerialNumber = "A", Firmware = "new" });
        var preview = ExcelImportMergeService.Analyze(client, location, null, [same]);
        existing.Notes = "Changed after preview";
        var count = DeviceLimitPolicy.CountDevices(data);
        try { ExcelImportMergeService.Apply(client, location, null, preview); }
        catch (InvalidOperationException)
        {
            Assert(existing.Firmware.Length == 0 && DeviceLimitPolicy.CountDevices(data) == count,
                "A stale import preview partially changed inventory.");
            return;
        }
        throw new InvalidOperationException("QC: stale import preview was accepted.");
    }

    private static AppData Inventory(int count) => new()
    {
        Clients = [new ClientRecord { Name = "Providence", Locations =
            [new LocationRecord { Name = "CSC", Rooms =
                [new RoomRecord { Name = "23 hr Conf Rm", Equipment = Devices(count) }] }] }]
    };
    private static List<EquipmentRecord> Devices(int count) => Enumerable.Range(0, count)
        .Select(i =>
        {
            var device = new EquipmentRecord { Description = "Device " + i };
            device.EnsureNetworkInterfaces();
            return device;
        }).ToList();
    private static List<EquipmentContext> Contexts(AppData data) => data.Clients.SelectMany(client =>
        client.Locations.SelectMany(location => location.Rooms.SelectMany(room =>
            room.Equipment.Select(device => new EquipmentContext(client, location, room, device))))).ToList();
    private static AppData Clone(AppData data) => JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("QC: " + message);
    }
}
