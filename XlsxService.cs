using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace InNasc;

internal sealed record ExcelImportSkippedRow(
    string SourceFile,
    string Worksheet,
    int RowNumber,
    string RowPreview,
    string Reason);

internal sealed record ExcelImportSheetIssue(
    string SourceFile,
    string Worksheet,
    int NonEmptyRows,
    string Reason);

internal sealed record ExcelWorkbookImportScan(
    string SourcePath,
    string SourceFile,
    int WorksheetCount,
    int RecognizedWorksheetCount,
    int EmptyRowsIgnored,
    IReadOnlyList<ImportedEquipment> ImportedRows,
    IReadOnlyList<ExcelImportSkippedRow> SkippedRows,
    IReadOnlyList<ExcelImportSheetIssue> SheetIssues)
{
    public int CandidateRowsScanned => ImportedRows.Count + SkippedRows.Count;
    public int UnrecognizedSheetRows => SheetIssues.Sum(issue => issue.NonEmptyRows);
}

public static class XlsxService
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<ImportedEquipment> Import(string filePath) =>
        ScanImport(filePath).ImportedRows.ToList();

    internal static ExcelWorkbookImportScan ScanImport(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var sharedStrings = ReadSharedStrings(archive);
        var sheets = FindWorksheets(archive);
        var imported = new List<ImportedEquipment>();
        var skippedRows = new List<ExcelImportSkippedRow>();
        var sheetIssues = new List<ExcelImportSheetIssue>();
        var recognizedWorksheetCount = 0;
        var emptyRowsIgnored = 0;
        var sourceFile = Path.GetFileName(filePath);

        foreach (var sheet in sheets)
        {
            var entry = archive.GetEntry(sheet.Path);
            if (entry is null)
            {
                sheetIssues.Add(new ExcelImportSheetIssue(
                    sourceFile,
                    sheet.Name,
                    0,
                    $"The worksheet data at '{sheet.Path}' is missing from the workbook."));
                continue;
            }
            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            var rows = document.Descendants(Spreadsheet + "row")
                .Select(row => new WorksheetRow(
                    int.TryParse((string?)row.Attribute("r"), out var rowNumber)
                        ? rowNumber
                        : 0,
                    ReadRow(row, sharedStrings)))
                .ToList();
            if (rows.Count == 0) continue;

            var headerIndex = FindHeaderRow(rows);
            if (headerIndex < 0)
            {
                var nonEmptyRows = rows.Count(row =>
                    row.Values.Values.Any(value => !string.IsNullOrWhiteSpace(value)));
                if (nonEmptyRows > 0)
                {
                    sheetIssues.Add(new ExcelImportSheetIssue(
                        sourceFile,
                        sheet.Name,
                        nonEmptyRows,
                        "No supported equipment header row was recognized, so this sheet was not imported."));
                }
                continue;
            }
            recognizedWorksheetCount++;
            var headers = rows[headerIndex].Values
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Value))
                .GroupBy(cell => NormalizeHeader(cell.Value))
                .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);

            for (var rowIndex = headerIndex + 1; rowIndex < rows.Count; rowIndex++)
            {
                var worksheetRow = rows[rowIndex];
                var row = worksheetRow.Values;
                var actualRowNumber = worksheetRow.RowNumber > 0
                    ? worksheetRow.RowNumber
                    : rowIndex + 1;
                if (!row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    emptyRowsIgnored++;
                    continue;
                }
                if (HeaderMatchScore(row) >= 2)
                {
                    skippedRows.Add(new ExcelImportSkippedRow(
                        sourceFile,
                        sheet.Name,
                        actualRowNumber,
                        PreviewRow(row),
                        "Repeated header row."));
                    continue;
                }

                string Get(params string[] aliases)
                {
                    foreach (var alias in aliases)
                    {
                        if (headers.TryGetValue(NormalizeHeader(alias), out var column) &&
                            row.TryGetValue(column, out var value) &&
                            !string.IsNullOrWhiteSpace(value))
                            return Clean(value);
                    }
                    return string.Empty;
                }

                var description = Get("description", "equipment type", "device type", "equipment");
                var manufacturer = Get("manufacturer", "make", "brand", "vendor");
                var partNumber = Get(
                    "part number", "part no", "part #", "model", "model number", "model no", "model #");
                var equipmentId = Get(
                    "equipment id", "equipment i.d.", "device id", "asset id", "asset tag", "id");
                var hostname = Get("hostname", "host name", "device name", "network name");
                var serial = Get(
                    "serial number", "serial #", "serial no", "serial", "s/n", "sn");
                var primaryIpRaw = Get(
                    "ip address new", "primary ip", "primary ip address", "ip address",
                    "ip address main", "main ip", "control ip", "control ip address");
                var secondaryIpRaw = Get(
                    "secondary ip", "secondary ip address", "ip address sec",
                    "ip address secondary", "second ip", "ip 2");
                var targetIpRaw = Get("target ip", "new ip", "ip 3");
                var danteIpRaw = Get("dante ip", "dante address", "dante ip address");
                var primaryIp = Ipv4AddressText.NormalizeOrOriginal(primaryIpRaw);
                var secondaryIp = Ipv4AddressText.NormalizeOrOriginal(secondaryIpRaw);
                var targetIp = Ipv4AddressText.NormalizeOrOriginal(targetIpRaw);
                var danteIp = Ipv4AddressText.NormalizeOrOriginal(danteIpRaw);
                var subnet = Get("subnet", "subnet mask", "netmask");
                var gateway = Get("gateway", "default gateway", "router");
                var mac1Raw = Get(
                    "expected mac", "mac address main", "mac address 1", "mac address",
                    "main mac", "mac 1", "mac");
                var mac2Raw = Get(
                    "mac address sec", "ma address sec", "mac address 2", "secondary mac", "mac 2");
                var mac3Raw = Get("mac address 3", "mac 3");
                var mac1 = MacAddressText.NormalizeOrOriginal(mac1Raw);
                var mac2 = MacAddressText.NormalizeOrOriginal(mac2Raw);
                var mac3 = MacAddressText.NormalizeOrOriginal(mac3Raw);
                var interfaceTypeText = Get("interface type", "network type", "ip type");
                var firmware = Get("firmware software version", "firmware", "software version");
                var serialConnection = Get("serial connection", "control connection");
                var username = Get("user name", "username", "login");
                var password = Get("password", "passcode");
                var notes = Get("notes", "notes:", "comments");
                var roomName = Get("location", "room", "room name", "area", "space");

                if (string.IsNullOrWhiteSpace(description))
                    description = FirstNonEmpty(partNumber, equipmentId, hostname);
                var hasEquipmentIdentity = new[]
                {
                    description, manufacturer, partNumber, equipmentId, hostname, serial,
                    primaryIp, secondaryIp, targetIp, danteIp, mac1, mac2, mac3
                }.Any(value => !string.IsNullOrWhiteSpace(value));
                if (!hasEquipmentIdentity)
                {
                    skippedRows.Add(new ExcelImportSkippedRow(
                        sourceFile,
                        sheet.Name,
                        actualRowNumber,
                        PreviewRow(row),
                        "The row contains no supported equipment identity, IP, or MAC value."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(description))
                    description = $"Unnamed equipment — {sheet.Name} row {actualRowNumber}";

                var warnings = new List<string>();
                AddInvalidIpWarning(warnings, "Primary IP", primaryIpRaw);
                AddInvalidIpWarning(warnings, "Secondary IP", secondaryIpRaw);
                AddInvalidIpWarning(warnings, "Target IP", targetIpRaw);
                AddInvalidIpWarning(warnings, "Dante IP", danteIpRaw);
                AddInvalidMacWarning(warnings, "Main MAC", mac1Raw);
                AddInvalidMacWarning(warnings, "Secondary MAC", mac2Raw);
                AddInvalidMacWarning(warnings, "Third MAC", mac3Raw);

                var equipment = new EquipmentRecord
                {
                    Description = description,
                    Manufacturer = manufacturer,
                    PartNumber = partNumber,
                    EquipmentId = equipmentId,
                    Hostname = hostname,
                    SerialNumber = serial,
                    Firmware = firmware,
                    PrimaryIp = primaryIp,
                    SecondaryIp = secondaryIp,
                    TargetIp = targetIp,
                    DanteIp = danteIp,
                    Subnet = subnet,
                    Gateway = gateway,
                    Mac1 = mac1,
                    Mac2 = mac2,
                    Mac3 = mac3,
                    SerialConnection = serialConnection,
                    Username = username,
                    Password = password,
                    Notes = notes,
                    SourceFile = sourceFile,
                    NetworkState = string.IsNullOrWhiteSpace(primaryIp) ? NetworkState.NoAddress : NetworkState.Unknown
                };
                if (!string.IsNullOrWhiteSpace(interfaceTypeText) &&
                    Enum.TryParse<NetworkInterfaceType>(interfaceTypeText.Replace(" ", string.Empty),
                        ignoreCase: true, out var importedType))
                {
                    equipment.NetworkInterfaces =
                    [
                        new NetworkInterfaceRecord
                        {
                            Type = importedType,
                            IpAddress = primaryIp,
                            MacAddress = mac1,
                            NetworkState = string.IsNullOrWhiteSpace(primaryIp)
                                ? NetworkState.NoAddress
                                : NetworkState.Unknown
                        }
                    ];
                }
                equipment.EnsureNetworkInterfaces();
                imported.Add(new ImportedEquipment(
                    roomName,
                    equipment,
                    sourceFile,
                    sheet.Name,
                    actualRowNumber,
                    warnings));
            }
        }

        return new ExcelWorkbookImportScan(
            filePath,
            sourceFile,
            sheets.Count,
            recognizedWorksheetCount,
            emptyRowsIgnored,
            imported,
            skippedRows,
            sheetIssues);
    }

    public static int Export(string filePath, AppData data) =>
        Export(filePath, data, data.Clients);

    public static int ExportClient(string filePath, AppData data, ClientRecord client) =>
        Export(filePath, data, [client]);

    private static int Export(
        string filePath,
        AppData data,
        IReadOnlyCollection<ClientRecord> clients)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Excel exports must use the .xlsx file extension.", nameof(filePath));

        var contexts = Flatten(clients).ToList();
        var workbookTitle = clients.Count == 1
            ? clients.First().Name
            : "InNasc Client Export";
        var sheets = new List<ExportSheet>
        {
            BuildSummary(clients, contexts, workbookTitle)
        };
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Project Summary"
        };
        foreach (var client in clients.OrderBy(client => client.Name))
        foreach (var location in client.Locations.OrderBy(location => location.Name))
        {
            var requestedName = clients.Count == 1
                ? location.Name
                : $"{client.Name} - {location.Name}";
            var sheetName = UniqueSheetName(requestedName, usedSheetNames);
            sheets.Add(BuildLocationSheet(client, location, sheetName));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory ?? Path.GetTempPath(), $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                WriteText(archive, "[Content_Types].xml", ContentTypesXml(sheets.Count));
                WriteText(archive, "_rels/.rels", PackageRelationshipsXml());
                WriteText(archive, "docProps/app.xml", AppPropertiesXml(sheets));
                WriteText(archive, "docProps/core.xml", CorePropertiesXml(workbookTitle));
                WriteText(archive, "xl/workbook.xml", WorkbookXml(sheets));
                WriteText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml(sheets.Count));
                WriteText(archive, "xl/styles.xml", StylesXml());
                for (var i = 0; i < sheets.Count; i++)
                    WriteText(archive, $"xl/worksheets/sheet{i + 1}.xml", WorksheetXml(sheets[i]));
            }
            ValidateExport(temporary, sheets.Count, contexts.Count);
            File.Move(temporary, filePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return contexts.Count;
    }

    private static IEnumerable<EquipmentContext> Flatten(IEnumerable<ClientRecord> clients)
    {
        foreach (var client in clients)
        foreach (var location in client.Locations)
        foreach (var room in location.Rooms)
        foreach (var equipment in room.Equipment)
            yield return new EquipmentContext(client, location, room, equipment);
    }

    private static ExportSheet BuildSummary(
        IReadOnlyCollection<ClientRecord> clients,
        IReadOnlyCollection<EquipmentContext> contexts,
        string workbookTitle)
    {
        var rows = new List<ExportRow>
        {
            ExportRow.Title(workbookTitle, 7),
            ExportRow.Header("Client", "Address", "Client Notes", "Locations", "Rooms", "Equipment")
        };
        foreach (var client in clients.OrderBy(client => client.Name))
        {
            rows.Add(ExportRow.Body(
                client.Name,
                client.Address,
                client.Notes,
                client.Locations.Count,
                client.Locations.Sum(location => location.Rooms.Count),
                contexts.Count(item => ReferenceEquals(item.Client, client))));
        }

        rows.Add(ExportRow.Blank(7));
        rows.Add(ExportRow.Header(
            "Client", "Location", "Location Address", "Room", "Equipment", "Location Notes", "Room Notes"));

        foreach (var client in clients.OrderBy(client => client.Name))
        foreach (var location in client.Locations.OrderBy(location => location.Name))
        {
            if (location.Rooms.Count == 0)
            {
                rows.Add(ExportRow.Body(client.Name, location.Name, location.Address, string.Empty, 0,
                    location.Notes, string.Empty));
                continue;
            }

            foreach (var room in location.Rooms.OrderBy(room => room.Name))
                rows.Add(ExportRow.Body(client.Name, location.Name, location.Address, room.Name,
                    room.Equipment.Count, location.Notes, room.Notes));
        }

        return new ExportSheet("Project Summary", rows, [24, 24, 28, 20, 14, 34, 34], 2, false);
    }

    private static void ValidateExport(string filePath, int sheetCount, int equipmentCount)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var requiredEntries = new List<string>
        {
            "[Content_Types].xml",
            "_rels/.rels",
            "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels",
            "xl/styles.xml"
        };
        requiredEntries.AddRange(Enumerable.Range(1, sheetCount)
            .Select(index => $"xl/worksheets/sheet{index}.xml"));
        var missing = requiredEntries.FirstOrDefault(entry => archive.GetEntry(entry) is null);
        if (missing is not null)
            throw new InvalidDataException($"The Excel workbook is missing {missing}.");

        var workbookEntry = archive.GetEntry("xl/workbook.xml")!;
        using (var stream = workbookEntry.Open())
        {
            var workbook = XDocument.Load(stream);
            var exportedSheets = workbook.Descendants(Spreadsheet + "sheet").Count();
            if (exportedSheets != sheetCount)
                throw new InvalidDataException(
                    $"The workbook contains {exportedSheets:N0} sheets; {sheetCount:N0} were expected.");
        }

        var exportedEquipment = 0;
        for (var index = 2; index <= sheetCount; index++)
        {
            var sheetEntry = archive.GetEntry($"xl/worksheets/sheet{index}.xml")!;
            using var stream = sheetEntry.Open();
            var sheet = XDocument.Load(stream);
            exportedEquipment += Math.Max(0, sheet.Descendants(Spreadsheet + "row").Count() - 2);
        }
        if (exportedEquipment != equipmentCount)
            throw new InvalidDataException(
                $"The workbook contains {exportedEquipment:N0} equipment rows; {equipmentCount:N0} were expected.");
    }

    private static ExportSheet BuildLocationSheet(
        ClientRecord client,
        LocationRecord location,
        string sheetName)
    {
        var rows = new List<ExportRow>
        {
            ExportRow.Title($"{client.Name} — {location.Name}", 14),
            ExportRow.Header(
                "Room", "Description", "Manufacturer", "Hostname", "Serial Number", "Firmware",
                "Primary IP", "Secondary IPs", "MAC Addresses", "Subnet", "Gateway",
                "User Name", "Password", "Notes")
        };

        foreach (var room in location.Rooms.OrderBy(room => room.Name))
        foreach (var equipment in room.Equipment.OrderBy(equipment => equipment.Description)
                     .ThenBy(equipment => equipment.Manufacturer)
                     .ThenBy(equipment => equipment.Hostname))
        {
            equipment.EnsureNetworkInterfaces();
            var interfaces = equipment.NetworkInterfaces.Where(networkInterface =>
                !string.IsNullOrWhiteSpace(networkInterface.IpAddress) ||
                !string.IsNullOrWhiteSpace(networkInterface.MacAddress)).ToList();
            var primary = interfaces.FirstOrDefault(networkInterface =>
                              networkInterface.Type == NetworkInterfaceType.Main &&
                              !string.IsNullOrWhiteSpace(networkInterface.IpAddress))
                          ?? interfaces.FirstOrDefault(networkInterface =>
                              !string.IsNullOrWhiteSpace(networkInterface.IpAddress));
            var secondaryIps = interfaces
                .Where(networkInterface => !ReferenceEquals(networkInterface, primary) &&
                                           !string.IsNullOrWhiteSpace(networkInterface.IpAddress))
                .DistinctBy(networkInterface => networkInterface.IpAddress, StringComparer.OrdinalIgnoreCase)
                .Select(networkInterface => $"{networkInterface.Type}: {networkInterface.IpAddress}");
            var macAddresses = interfaces
                .Where(networkInterface => !string.IsNullOrWhiteSpace(networkInterface.MacAddress))
                .DistinctBy(networkInterface => networkInterface.MacAddress, StringComparer.OrdinalIgnoreCase)
                .Select(networkInterface => string.IsNullOrWhiteSpace(networkInterface.IpAddress)
                    ? $"{networkInterface.Type}: {networkInterface.MacAddress}"
                    : $"{networkInterface.Type} ({networkInterface.IpAddress}): {networkInterface.MacAddress}");

            rows.Add(ExportRow.Body(
                room.Name,
                equipment.Description,
                equipment.Manufacturer,
                equipment.Hostname,
                equipment.SerialNumber,
                equipment.Firmware,
                primary?.IpAddress ?? string.Empty,
                string.Join("\n", secondaryIps),
                string.Join("\n", macAddresses),
                equipment.Subnet,
                equipment.Gateway,
                equipment.Username,
                equipment.Password,
                equipment.Notes));
        }
        return new ExportSheet(sheetName, rows,
            [18, 34, 20, 20, 20, 16, 18, 42, 48, 18, 18, 20, 20, 44], 2, true);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(Spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(text => text.Value)))
            .ToList();
    }

    private static IReadOnlyList<WorkbookSheet> FindWorksheets(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml") ??
                            throw new InvalidDataException("The workbook does not contain xl/workbook.xml.");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ??
                                 throw new InvalidDataException("The workbook relationship file is missing.");
        XDocument workbook;
        XDocument relationships;
        using (var stream = workbookEntry.Open()) workbook = XDocument.Load(stream);
        using (var stream = relationshipsEntry.Open()) relationships = XDocument.Load(stream);
        var targets = relationships.Descendants(PackageRelationships + "Relationship")
            .Where(item => ((string?)item.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true)
            .ToDictionary(item => (string)item.Attribute("Id")!, item => (string)item.Attribute("Target")!);

        var result = new List<WorkbookSheet>();
        foreach (var sheet in workbook.Descendants(Spreadsheet + "sheet"))
        {
            var relationshipId = (string?)sheet.Attribute(OfficeRelationships + "id");
            if (relationshipId is null || !targets.TryGetValue(relationshipId, out var target)) continue;
            var normalized = NormalizeWorkbookTarget(target);
            result.Add(new WorkbookSheet((string?)sheet.Attribute("name") ?? "Sheet", normalized));
        }
        return result;
    }

    private static string NormalizeWorkbookTarget(string target)
    {
        var normalized = target.Replace('\\', '/').TrimStart('/');
        var combined = normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"xl/{normalized}";
        var segments = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return string.Join("/", segments);
    }

    private static Dictionary<int, string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var values = new Dictionary<int, string>();
        var fallbackColumn = 0;
        foreach (var cell in row.Elements(Spreadsheet + "c"))
        {
            var reference = (string?)cell.Attribute("r");
            var column = reference is null ? fallbackColumn : ColumnIndex(reference);
            fallbackColumn = column + 1;
            var type = (string?)cell.Attribute("t");
            string value;
            if (type == "inlineStr")
                value = string.Concat(cell.Descendants(Spreadsheet + "t").Select(text => text.Value));
            else
            {
                value = cell.Element(Spreadsheet + "v")?.Value ?? string.Empty;
                if (type == "s" && int.TryParse(value, out var sharedIndex) &&
                    sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                    value = sharedStrings[sharedIndex];
            }
            values[column] = value;
        }
        return values;
    }

    private static int FindHeaderRow(IReadOnlyList<WorksheetRow> rows)
    {
        var bestIndex = -1;
        var bestScore = 0;
        for (var index = 0; index < Math.Min(rows.Count, 100); index++)
        {
            var score = HeaderMatchScore(rows[index].Values);
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = index;
        }
        return bestScore >= 2 ? bestIndex : -1;
    }

    private static int HeaderMatchScore(IReadOnlyDictionary<int, string> row)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "description", "manufacturer", "make", "brand", "vendor",
            "equipment type", "device type", "equipment", "device",
            "part number", "part no", "part", "model", "model number", "model no",
            "hostname", "host name", "device name", "network name",
            "location", "room", "room name", "area", "space",
            "mac address", "mac address main", "main mac", "mac address 1",
            "ip address", "ip address main", "ip address new", "primary ip", "main ip",
            "serial number", "serial no", "serial", "equipment id", "device id",
            "asset id", "asset tag", "firmware", "firmware software version",
            "username", "user name", "password", "notes"
        };
        return row.Values.Select(NormalizeHeader).Count(known.Contains);
    }

    private static int ColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character)) break;
            index = index * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return Math.Max(0, index - 1);
    }

    private static string NormalizeHeader(string value)
    {
        var builder = new StringBuilder();
        var lastWasSpace = false;
        foreach (var character in value.Replace('\u00A0', ' ').Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    private static string Clean(string value) => value.Replace('\u00A0', ' ').Trim();

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string PreviewRow(IReadOnlyDictionary<int, string> row)
    {
        var values = row.OrderBy(cell => cell.Key)
            .Select(cell => Clean(cell.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(4)
            .ToList();
        if (values.Count == 0) return "(empty row)";
        var preview = string.Join(" | ", values);
        return preview.Length <= 140 ? preview : preview[..137] + "…";
    }

    private static void AddInvalidIpWarning(
        ICollection<string> warnings,
        string fieldName,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !Ipv4AddressText.TryParse(value, out _, out _))
            warnings.Add($"{fieldName} '{value}' is not a valid IPv4 address.");
    }

    private static void AddInvalidMacWarning(
        ICollection<string> warnings,
        string fieldName,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !MacAddressText.TryParse(value, out _))
            warnings.Add($"{fieldName} '{value}' is not a valid MAC address.");
    }

    private static string UniqueSheetName(string requestedName, ISet<string> usedNames)
    {
        var invalidCharacters = new HashSet<char>(['[', ']', ':', '*', '?', '/', '\\']);
        var cleaned = new string(requestedName
                .Select(character => invalidCharacters.Contains(character) ? ' ' : character)
                .ToArray())
            .Trim()
            .Trim('\'')
            .Trim();
        cleaned = string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Location";

        var baseName = cleaned[..Math.Min(31, cleaned.Length)];
        var candidate = baseName;
        var suffixNumber = 2;
        while (!usedNames.Add(candidate))
        {
            var suffix = $" ({suffixNumber++})";
            candidate = baseName[..Math.Min(baseName.Length, 31 - suffix.Length)] + suffix;
        }
        return candidate;
    }

    private static string StatusText(NetworkState state) => state switch
    {
        NetworkState.Reachable => "Online",
        NetworkState.Unreachable => "Offline",
        NetworkState.NoAddress => "No IP",
        NetworkState.Partial => "Partially online",
        NetworkState.MacMismatch => "MAC mismatch",
        _ => "Waiting to verify"
    };

    private static string LocalTime(DateTime? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    private static string IpSortValue(string ip) => string.Join('.', ip.Split('.').Select(part =>
        int.TryParse(part, out var value) ? value.ToString("D3") : part));

    private static void WriteText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ContentTypesXml(int sheetCount)
    {
        var sheetOverrides = string.Concat(Enumerable.Range(1, sheetCount).Select(index =>
            $"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
               "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
               "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
               "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
               "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
               "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
               "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
               sheetOverrides + "</Types>";
    }

    private static string PackageRelationshipsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
        "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
        "</Relationships>";

    private static string WorkbookXml(IReadOnlyList<ExportSheet> sheets)
    {
        var sheetXml = string.Concat(sheets.Select((sheet, index) =>
            $"<sheet name=\"{EscapeAttribute(sheet.Name)}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
               $"<sheets>{sheetXml}</sheets></workbook>";
    }

    private static string WorkbookRelationshipsXml(int sheetCount)
    {
        var relationships = string.Concat(Enumerable.Range(1, sheetCount).Select(index =>
            $"<Relationship Id=\"rId{index}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{index}.xml\"/>"));
        relationships += $"<Relationship Id=\"rId{sheetCount + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{relationships}</Relationships>";
    }

    private static string StylesXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"3\">" +
        "<font><sz val=\"10\"/><name val=\"Aptos\"/><family val=\"2\"/></font>" +
        "<font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"16\"/><name val=\"Aptos Display\"/></font>" +
        "<font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"10\"/><name val=\"Aptos\"/></font>" +
        "</fonts>" +
        "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0F172A\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2563EB\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left style=\"thin\"><color rgb=\"FFDDE3EC\"/></left><right style=\"thin\"><color rgb=\"FFDDE3EC\"/></right>" +
        "<top style=\"thin\"><color rgb=\"FFDDE3EC\"/></top><bottom style=\"thin\"><color rgb=\"FFDDE3EC\"/></bottom><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"4\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"><alignment vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
        "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static string WorksheetXml(ExportSheet sheet)
    {
        var maxColumn = Math.Max(1, sheet.Rows.Max(row => row.Values.Count));
        var dimension = $"A1:{ColumnName(maxColumn - 1)}{Math.Max(1, sheet.Rows.Count)}";
        var columns = string.Concat(sheet.ColumnWidths.Select((width, index) =>
            $"<col min=\"{index + 1}\" max=\"{index + 1}\" width=\"{width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>"));
        var rowXml = new StringBuilder();
        for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
        {
            var row = sheet.Rows[rowIndex];
            var rowNumber = rowIndex + 1;
            var height = RowHeight(sheet, row);
            rowXml.Append($"<row r=\"{rowNumber}\" ht=\"{height}\" customHeight=\"1\">");
            for (var columnIndex = 0; columnIndex < row.Values.Count; columnIndex++)
            {
                var cellReference = $"{ColumnName(columnIndex)}{rowNumber}";
                var value = row.Values[columnIndex];
                if (value is null || value is string text && text.Length == 0)
                {
                    rowXml.Append($"<c r=\"{cellReference}\" s=\"{row.Style}\"/>");
                    continue;
                }
                if (value is byte or short or int or long or float or double or decimal)
                {
                    rowXml.Append($"<c r=\"{cellReference}\" s=\"{row.Style}\" t=\"n\"><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>");
                    continue;
                }
                rowXml.Append($"<c r=\"{cellReference}\" s=\"{row.Style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{EscapeText(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}</t></is></c>");
            }
            rowXml.Append("</row>");
        }

        var merge = sheet.Rows.Count > 0 && sheet.Rows[0].Values.Count > 1
            ? $"<mergeCells count=\"1\"><mergeCell ref=\"A1:{ColumnName(maxColumn - 1)}1\"/></mergeCells>"
            : string.Empty;
        var filter = sheet.AutoFilter && sheet.Rows.Count >= sheet.HeaderRow
            ? $"<autoFilter ref=\"A{sheet.HeaderRow}:{ColumnName(maxColumn - 1)}{sheet.Rows.Count}\"/>"
            : string.Empty;
        var pane = sheet.HeaderRow > 0
            ? $"<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"{sheet.HeaderRow}\" topLeftCell=\"A{sheet.HeaderRow + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>"
            : "<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
               $"<dimension ref=\"{dimension}\"/>{pane}<sheetFormatPr defaultRowHeight=\"15\"/><cols>{columns}</cols>" +
               $"<sheetData>{rowXml}</sheetData>{filter}{merge}</worksheet>";
    }

    private static double RowHeight(ExportSheet sheet, ExportRow row)
    {
        if (row.Style == 1) return 30;
        if (row.Style == 2) return 28;
        if (row.Style == 0) return 14;

        var requiredLines = 1;
        for (var index = 0; index < row.Values.Count; index++)
        {
            var text = Convert.ToString(row.Values[index], CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text)) continue;
            var width = index < sheet.ColumnWidths.Length ? sheet.ColumnWidths[index] : 12;
            var charactersPerLine = Math.Max(8, (int)Math.Floor(width - 2));
            var estimatedLines = text.Replace("\r", string.Empty)
                .Split('\n')
                .Sum(line => Math.Max(1, (int)Math.Ceiling((double)line.Length / charactersPerLine)));
            requiredLines = Math.Max(requiredLines, estimatedLines);
        }
        return Math.Min(120, 22 + (requiredLines - 1) * 15);
    }

    private static string AppPropertiesXml(IReadOnlyList<ExportSheet> sheets)
    {
        var titles = string.Concat(sheets.Select(sheet => $"<vt:lpstr>{EscapeText(sheet.Name)}</vt:lpstr>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
               "<Application>InNasc</Application><DocSecurity>0</DocSecurity><ScaleCrop>false</ScaleCrop>" +
               $"<HeadingPairs><vt:vector size=\"2\" baseType=\"variant\"><vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant><vt:variant><vt:i4>{sheets.Count}</vt:i4></vt:variant></vt:vector></HeadingPairs>" +
               $"<TitlesOfParts><vt:vector size=\"{sheets.Count}\" baseType=\"lpstr\">{titles}</vt:vector></TitlesOfParts>" +
               "<Company>InN8 Labs</Company><LinksUpToDate>false</LinksUpToDate><SharedDoc>false</SharedDoc><HyperlinksChanged>false</HyperlinksChanged><AppVersion>16.0300</AppVersion></Properties>";
    }

    private static string CorePropertiesXml(string workbookTitle)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
               $"<dc:title>{EscapeText(workbookTitle)}</dc:title><dc:creator>InN8 Labs</dc:creator><cp:lastModifiedBy>InN8 Labs</cp:lastModifiedBy><dcterms:created xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{now}</dcterms:modified></cp:coreProperties>";
    }

    private static string EscapeText(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    private static string EscapeAttribute(string value) => EscapeText(value).Replace("\"", "&quot;");

    private static string ColumnName(int zeroBasedIndex)
    {
        var result = string.Empty;
        var index = zeroBasedIndex + 1;
        while (index > 0)
        {
            index--;
            result = (char)('A' + index % 26) + result;
            index /= 26;
        }
        return result;
    }

    private sealed record WorkbookSheet(string Name, string Path);
    private sealed record WorksheetRow(int RowNumber, Dictionary<int, string> Values);
    private sealed record ExportSheet(string Name, List<ExportRow> Rows, double[] ColumnWidths, int HeaderRow, bool AutoFilter);
    private sealed record ExportRow(IReadOnlyList<object?> Values, int Style)
    {
        public static ExportRow Title(string title, int columns) => new(
            new object?[] { title }.Concat(Enumerable.Repeat<object?>(string.Empty, Math.Max(0, columns - 1))).ToList(), 1);
        public static ExportRow Header(params object?[] values) => new(values, 2);
        public static ExportRow Body(params object?[] values) => new(values, 3);
        public static ExportRow Blank(int columns) => new(Enumerable.Repeat<object?>(string.Empty, columns).ToList(), 0);
    }
}
