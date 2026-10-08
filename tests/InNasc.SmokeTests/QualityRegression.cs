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
        var noAncestor = Clone(resumed);
        CheckoutResumeService.RefreshInventory(noAncestor, company, null);
        Assert(noAncestor.Clients[0].Locations[0].Rooms[0].Equipment[0].Notes == "Unpushed local work",
            "A missing ancestor must not erase unfinished checkout work.");

        RunImportRegression();
        Console.WriteLine("QC passed: scope rebinding, 169/172 counts, late upload edits, checkout resume, 240 sync cycles, import identity and preview races.");
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
