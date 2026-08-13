namespace InNasc;

internal enum ExcelImportAction
{
    AddNew,
    Merge,
    UnchangedDuplicate,
    Ambiguous
}

internal sealed record ExcelImportPreviewEntry(
    int Sequence,
    ExcelImportAction Action,
    ImportedEquipment ImportedRow,
    string DeviceName,
    string ExistingMatch,
    string Target,
    string Details)
{
    public string ActionLabel => Action switch
    {
        ExcelImportAction.AddNew => "NEW",
        ExcelImportAction.Merge => "MERGE",
        ExcelImportAction.UnchangedDuplicate => "DUPLICATE",
        ExcelImportAction.Ambiguous => "AMBIGUOUS",
        _ => Action.ToString().ToUpperInvariant()
    };
}

internal sealed record ExcelImportPlan(IReadOnlyList<ExcelImportPreviewEntry> Entries)
{
    public int ImportedRows => Entries.Count;
    public int AddedDevices => Entries.Count(item => item.Action == ExcelImportAction.AddNew);
    public int MergedDevices => Entries.Count(item => item.Action == ExcelImportAction.Merge);
    public int UnchangedDuplicates =>
        Entries.Count(item => item.Action == ExcelImportAction.UnchangedDuplicate);
    public int AmbiguousRows => Entries.Count(item => item.Action == ExcelImportAction.Ambiguous);
    public int ActionableRows => AddedDevices + MergedDevices;
}

internal sealed record ExcelImportMergeResult(
    int ImportedRows,
    int AddedDevices,
    int MergedDevices,
    int UnchangedDuplicates,
    int AmbiguousRows,
    int FilledBlankFields,
    int AddedNetworkInterfaces);

internal static class ExcelImportMergeService
{
    public static ExcelImportPlan Analyze(
        ClientRecord client,
        LocationRecord defaultLocation,
        RoomRecord? selectedRoom,
        IReadOnlyList<ImportedEquipment> importedRows)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(defaultLocation);
        ArgumentNullException.ThrowIfNull(importedRows);

        var simulation = ClientSubmatrixService.CloneClient(client);
        var simulationLocation = simulation.Locations.FirstOrDefault(
            item => item.Id == defaultLocation.Id);
        if (simulationLocation is null)
        {
            simulationLocation = new LocationRecord
            {
                Id = defaultLocation.Id,
                Name = defaultLocation.Name,
                Address = defaultLocation.Address,
                Notes = defaultLocation.Notes
            };
            simulation.Locations.Add(simulationLocation);
        }

        RoomRecord? simulationSelectedRoom = null;
        if (selectedRoom is not null)
        {
            simulationSelectedRoom = simulation.Locations
                .SelectMany(location => location.Rooms)
                .FirstOrDefault(room => room.Id == selectedRoom.Id);
            if (simulationSelectedRoom is null)
            {
                simulationSelectedRoom = new RoomRecord
                {
                    Id = selectedRoom.Id,
                    Name = selectedRoom.Name,
                    Notes = selectedRoom.Notes
                };
                simulationLocation.Rooms.Add(simulationSelectedRoom);
            }
        }

        var entries = new List<ExcelImportPreviewEntry>();
        for (var index = 0; index < importedRows.Count; index++)
        {
            var importedRow = importedRows[index];
            var imported = importedRow.Equipment;
            imported.EnsureNetworkInterfaces();
            var match = FindMatchingEquipment(simulation, imported);
            var deviceName = DeviceName(imported);
            var warnings = importedRow.ImportWarnings.Count == 0
                ? string.Empty
                : $" Warning: {string.Join(" ", importedRow.ImportWarnings)}";

            if (match.IsAmbiguous)
            {
                entries.Add(new ExcelImportPreviewEntry(
                    index,
                    ExcelImportAction.Ambiguous,
                    importedRow,
                    deviceName,
                    string.Join("; ", match.TiedCandidates.Select(candidate =>
                        EquipmentPath(simulation, candidate.Equipment))),
                    string.Empty,
                    $"{match.TiedCandidates.Count:N0} existing devices have the same strongest match " +
                    $"({match.BestEvidenceSummary}). Nothing will be changed for this row.{warnings}"));
                continue;
            }

            if (match.Equipment is null)
            {
                var room = ResolveImportRoom(
                    simulationLocation,
                    simulationSelectedRoom,
                    importedRow.RoomName);
                var clone = ClientSubmatrixService.CloneEquipment(imported);
                room.Equipment.Add(clone);
                entries.Add(new ExcelImportPreviewEntry(
                    index,
                    ExcelImportAction.AddNew,
                    importedRow,
                    deviceName,
                    string.Empty,
                    $"{simulationLocation.Name} / {room.Name}",
                    $"No existing device matched. A new device will be added.{warnings}"));
                continue;
            }

            var existingPath = EquipmentPath(simulation, match.Equipment);
            var enrichment = EnrichExisting(match.Equipment, imported);
            var details = enrichment.HasChanges
                ? BuildEnrichmentDetails(enrichment, match.BestEvidenceSummary)
                : $"Already present; matched by {match.BestEvidenceSummary}. " +
                  "Existing values remain unchanged.";
            entries.Add(new ExcelImportPreviewEntry(
                index,
                enrichment.HasChanges
                    ? ExcelImportAction.Merge
                    : ExcelImportAction.UnchangedDuplicate,
                importedRow,
                deviceName,
                existingPath,
                existingPath,
                details + warnings));
        }

        return new ExcelImportPlan(entries);
    }

    public static ExcelImportMergeResult Apply(
        ClientRecord client,
        LocationRecord defaultLocation,
        RoomRecord? selectedRoom,
        ExcelImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(defaultLocation);
        ArgumentNullException.ThrowIfNull(plan);

        var validation = Analyze(
            client,
            defaultLocation,
            selectedRoom,
            plan.Entries.OrderBy(item => item.Sequence)
                .Select(item => item.ImportedRow)
                .ToList());
        var plannedEntries = plan.Entries.OrderBy(item => item.Sequence).ToList();
        if (validation.Entries.Count != plannedEntries.Count)
            throw new InvalidOperationException(
                "The number of Excel rows changed after the preview. " +
                "No import was applied; reopen the preview.");
        for (var index = 0; index < validation.Entries.Count; index++)
        {
            if (validation.Entries[index].Action == plannedEntries[index].Action) continue;
            throw new InvalidOperationException(
                $"The match for {validation.Entries[index].DeviceName} changed after the preview. " +
                "No import was applied; reopen the preview and review the new result.");
        }

        var workingClient = ClientSubmatrixService.CloneClient(client);
        var workingLocation = workingClient.Locations.FirstOrDefault(location =>
            location.Id == defaultLocation.Id);
        if (workingLocation is null)
        {
            workingLocation = new LocationRecord
            {
                Id = defaultLocation.Id,
                Name = defaultLocation.Name,
                Address = defaultLocation.Address,
                Notes = defaultLocation.Notes
            };
            workingClient.Locations.Add(workingLocation);
        }
        RoomRecord? workingSelectedRoom = null;
        if (selectedRoom is not null)
        {
            workingSelectedRoom = workingClient.Locations
                .SelectMany(location => location.Rooms)
                .FirstOrDefault(room => room.Id == selectedRoom.Id);
            if (workingSelectedRoom is null)
            {
                workingSelectedRoom = new RoomRecord
                {
                    Id = selectedRoom.Id,
                    Name = selectedRoom.Name,
                    Notes = selectedRoom.Notes
                };
                workingLocation.Rooms.Add(workingSelectedRoom);
            }
        }

        var addedDevices = 0;
        var mergedDevices = 0;
        var unchangedDuplicates = 0;
        var ambiguousRows = 0;
        var filledBlankFields = 0;
        var addedNetworkInterfaces = 0;

        foreach (var entry in plan.Entries.OrderBy(item => item.Sequence))
        {
            var imported = entry.ImportedRow.Equipment;
            imported.EnsureNetworkInterfaces();
            var match = FindMatchingEquipment(workingClient, imported);
            switch (entry.Action)
            {
                case ExcelImportAction.AddNew:
                {
                    var room = ResolveImportRoom(
                        workingLocation,
                        workingSelectedRoom,
                        entry.ImportedRow.RoomName);
                    room.Equipment.Add(ClientSubmatrixService.CloneEquipment(imported));
                    addedDevices++;
                    break;
                }
                case ExcelImportAction.Merge:
                {
                    var enrichment = EnrichExisting(match.Equipment!, imported);
                    filledBlankFields += enrichment.FilledFields.Count;
                    addedNetworkInterfaces += enrichment.AddedNetworkInterfaces;
                    mergedDevices++;
                    break;
                }
                case ExcelImportAction.UnchangedDuplicate:
                    unchangedDuplicates++;
                    break;
                case ExcelImportAction.Ambiguous:
                    ambiguousRows++;
                    break;
            }
        }

        client.Locations = workingClient.Locations;
        return new ExcelImportMergeResult(
            plan.ImportedRows,
            addedDevices,
            mergedDevices,
            unchangedDuplicates,
            ambiguousRows,
            filledBlankFields,
            addedNetworkInterfaces);
    }

    private static MatchResolution FindMatchingEquipment(
        ClientRecord client,
        EquipmentRecord imported)
    {
        var candidates = client.Locations
            .SelectMany(location => location.Rooms)
            .SelectMany(room => room.Equipment)
            .Select(existing => new MatchCandidate(existing, Score(existing, imported)))
            .Where(candidate => candidate.Evidence.IsMatch)
            .OrderByDescending(candidate => candidate.Evidence.Score)
            .ThenBy(candidate => candidate.Equipment.CreatedUtc)
            .ToList();
        if (candidates.Count == 0)
            return new MatchResolution(null, [], false, string.Empty);

        var best = candidates[0];
        var tied = candidates
            .Where(candidate => candidate.Evidence.Score == best.Evidence.Score)
            .ToList();
        if (tied.Count > 1)
            return new MatchResolution(
                null,
                tied,
                true,
                best.Evidence.EvidenceSummary);
        return new MatchResolution(
            best.Equipment,
            tied,
            false,
            best.Evidence.EvidenceSummary);
    }

    private static MatchEvidence Score(EquipmentRecord existing, EquipmentRecord imported)
    {
        existing.EnsureNetworkInterfaces();
        var score = 0;
        var hasUniqueIdentifier = false;
        var evidence = new List<string>();

        AddIdentifierMatch(existing.EquipmentId, imported.EquipmentId, 1000, "equipment ID");
        AddIdentifierMatch(existing.SerialNumber, imported.SerialNumber, 900, "serial number");
        if (Overlaps(
                existing.NetworkInterfaces.Select(item => NormalizeMac(item.MacAddress)),
                imported.NetworkInterfaces.Select(item => NormalizeMac(item.MacAddress))))
        {
            score += 800;
            hasUniqueIdentifier = true;
            evidence.Add("MAC address");
        }
        AddIdentifierMatch(existing.Hostname, imported.Hostname, 300, "hostname");
        if (Overlaps(
                existing.NetworkInterfaces.Select(item => NormalizeIp(item.IpAddress)),
                imported.NetworkInterfaces.Select(item => NormalizeIp(item.IpAddress))))
        {
            score += 250;
            hasUniqueIdentifier = true;
            evidence.Add("IP address");
        }

        var sameSignature =
            Same(existing.Manufacturer, imported.Manufacturer) &&
            Same(existing.PartNumber, imported.PartNumber) &&
            Same(existing.Description, imported.Description);
        if (sameSignature)
        {
            score += 200;
            evidence.Add("manufacturer/model/description");
        }
        if (Same(existing.PartNumber, imported.PartNumber)) score += 40;
        if (Same(existing.Description, imported.Description)) score += 20;
        if (Same(existing.Manufacturer, imported.Manufacturer)) score += 15;

        return new MatchEvidence(
            score,
            hasUniqueIdentifier || sameSignature,
            evidence.Count == 0 ? "device details" : string.Join(", ", evidence));

        void AddIdentifierMatch(
            string existingValue,
            string importedValue,
            int points,
            string name)
        {
            if (!Same(existingValue, importedValue)) return;
            score += points;
            hasUniqueIdentifier = true;
            evidence.Add(name);
        }
    }

    private static EnrichmentResult EnrichExisting(
        EquipmentRecord existing,
        EquipmentRecord imported)
    {
        existing.EnsureNetworkInterfaces();
        imported.EnsureNetworkInterfaces();

        var filledFields = new List<string>();
        FillBlank(existing.Description, imported.Description, value => existing.Description = value, "Description");
        FillBlank(existing.Manufacturer, imported.Manufacturer, value => existing.Manufacturer = value, "Manufacturer");
        FillBlank(existing.PartNumber, imported.PartNumber, value => existing.PartNumber = value, "Model / part number");
        FillBlank(existing.EquipmentId, imported.EquipmentId, value => existing.EquipmentId = value, "Equipment ID");
        FillBlank(existing.Hostname, imported.Hostname, value => existing.Hostname = value, "Hostname");
        FillBlank(existing.SerialNumber, imported.SerialNumber, value => existing.SerialNumber = value, "Serial number");
        FillBlank(existing.Firmware, imported.Firmware, value => existing.Firmware = value, "Firmware");
        FillBlank(existing.Subnet, imported.Subnet, value => existing.Subnet = value, "Subnet");
        FillBlank(existing.Gateway, imported.Gateway, value => existing.Gateway = value, "Gateway");
        FillBlank(
            existing.SerialConnection,
            imported.SerialConnection,
            value => existing.SerialConnection = value,
            "Serial connection");
        FillBlank(existing.Username, imported.Username, value => existing.Username = value, "Username");
        FillBlank(existing.Password, imported.Password, value => existing.Password = value, "Password");
        FillBlank(existing.Notes, imported.Notes, value => existing.Notes = value, "Notes");
        FillBlank(existing.SourceFile, imported.SourceFile, value => existing.SourceFile = value, "Source file");

        var addedInterfaces = 0;
        foreach (var importedInterface in imported.NetworkInterfaces.Where(HasInterfaceData))
        {
            var existingInterface = FindMatchingInterface(existing.NetworkInterfaces, importedInterface);
            if (existingInterface is null)
            {
                existing.NetworkInterfaces.Add(importedInterface.Clone());
                addedInterfaces++;
                continue;
            }

            FillBlank(
                existingInterface.IpAddress,
                importedInterface.IpAddress,
                value => existingInterface.IpAddress = Ipv4AddressText.NormalizeOrOriginal(value),
                $"{importedInterface.Type} IP address");
            FillBlank(
                existingInterface.MacAddress,
                importedInterface.MacAddress,
                value => existingInterface.MacAddress = MacAddressText.NormalizeOrOriginal(value),
                $"{importedInterface.Type} MAC address");
        }

        if (filledFields.Count > 0 || addedInterfaces > 0)
            existing.UpdatedUtc = DateTime.UtcNow;
        existing.SyncLegacyNetworkFields();
        existing.UpdateAggregateNetworkState();
        return new EnrichmentResult(filledFields, addedInterfaces);

        void FillBlank(
            string existingValue,
            string importedValue,
            Action<string> set,
            string fieldName)
        {
            if (!string.IsNullOrWhiteSpace(existingValue) ||
                string.IsNullOrWhiteSpace(importedValue))
                return;
            set(importedValue.Trim());
            filledFields.Add(fieldName);
        }
    }

    private static string BuildEnrichmentDetails(
        EnrichmentResult enrichment,
        string matchEvidence)
    {
        var changes = new List<string>();
        if (enrichment.FilledFields.Count > 0)
            changes.Add($"fill {string.Join(", ", enrichment.FilledFields.Distinct())}");
        if (enrichment.AddedNetworkInterfaces > 0)
            changes.Add($"add {enrichment.AddedNetworkInterfaces:N0} network interface(s)");
        return $"Matched by {matchEvidence}; existing values take priority. Will {string.Join(" and ", changes)}.";
    }

    private static NetworkInterfaceRecord? FindMatchingInterface(
        IReadOnlyList<NetworkInterfaceRecord> existingInterfaces,
        NetworkInterfaceRecord imported)
    {
        var importedIp = NormalizeIp(imported.IpAddress);
        var importedMac = NormalizeMac(imported.MacAddress);
        var exact = existingInterfaces.FirstOrDefault(existing =>
            (!string.IsNullOrWhiteSpace(importedMac) &&
             string.Equals(NormalizeMac(existing.MacAddress), importedMac, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(importedIp) &&
             string.Equals(NormalizeIp(existing.IpAddress), importedIp, StringComparison.OrdinalIgnoreCase)));
        if (exact is not null) return exact;

        return existingInterfaces.FirstOrDefault(existing =>
            existing.Type == imported.Type &&
            Compatible(existing.IpAddress, imported.IpAddress, NormalizeIp) &&
            Compatible(existing.MacAddress, imported.MacAddress, NormalizeMac) &&
            (string.IsNullOrWhiteSpace(existing.IpAddress) ||
             string.IsNullOrWhiteSpace(existing.MacAddress)));
    }

    private static bool Compatible(
        string existing,
        string imported,
        Func<string, string> normalize)
    {
        if (string.IsNullOrWhiteSpace(existing) || string.IsNullOrWhiteSpace(imported))
            return true;
        return string.Equals(normalize(existing), normalize(imported), StringComparison.OrdinalIgnoreCase);
    }

    private static RoomRecord ResolveImportRoom(
        LocationRecord location,
        RoomRecord? selectedRoom,
        string importedRoomName)
    {
        var roomName = string.IsNullOrWhiteSpace(importedRoomName)
            ? selectedRoom?.Name ?? "Imported Equipment"
            : importedRoomName.Trim();
        var room = location.Rooms.FirstOrDefault(item =>
            string.Equals(item.Name, roomName, StringComparison.CurrentCultureIgnoreCase));
        if (room is not null) return room;

        room = new RoomRecord { Name = roomName };
        location.Rooms.Add(room);
        return room;
    }

    private static string EquipmentPath(ClientRecord client, EquipmentRecord equipment)
    {
        foreach (var location in client.Locations)
        foreach (var room in location.Rooms)
        {
            if (room.Equipment.Any(item => ReferenceEquals(item, equipment) || item.Id == equipment.Id))
                return $"{location.Name} / {room.Name} / {DeviceName(equipment)}";
        }
        return DeviceName(equipment);
    }

    private static string DeviceName(EquipmentRecord equipment)
    {
        var identity = new[] { equipment.Description, equipment.PartNumber, equipment.Hostname }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "Unnamed equipment";
        return string.IsNullOrWhiteSpace(equipment.Manufacturer)
            ? identity
            : $"{equipment.Manufacturer} {identity}";
    }

    private static bool Same(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Overlaps(IEnumerable<string> left, IEnumerable<string> right)
    {
        var values = left
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return values.Count > 0 && right.Any(value =>
            !string.IsNullOrWhiteSpace(value) && values.Contains(value));
    }

    private static bool HasInterfaceData(NetworkInterfaceRecord item) =>
        !string.IsNullOrWhiteSpace(item.IpAddress) ||
        !string.IsNullOrWhiteSpace(item.MacAddress);

    private static string NormalizeIp(string value) =>
        Ipv4AddressText.NormalizeOrOriginal(value).Trim();

    private static string NormalizeMac(string value) =>
        MacAddressText.NormalizeOrOriginal(value).Trim();

    private sealed record MatchCandidate(EquipmentRecord Equipment, MatchEvidence Evidence);
    private sealed record MatchEvidence(
        int Score,
        bool IsMatch,
        string EvidenceSummary);
    private sealed record MatchResolution(
        EquipmentRecord? Equipment,
        IReadOnlyList<MatchCandidate> TiedCandidates,
        bool IsAmbiguous,
        string BestEvidenceSummary);
    private sealed record EnrichmentResult(
        IReadOnlyList<string> FilledFields,
        int AddedNetworkInterfaces)
    {
        public bool HasChanges => FilledFields.Count > 0 || AddedNetworkInterfaces > 0;
    }
}
