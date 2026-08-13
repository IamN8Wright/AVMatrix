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
                    description = $"Unnamed equipment â€” {sheet.Name} row {actualRowNumber}";

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
                WriteText(archive, "xl/styles.xml", Stylçm:¶‰žËkºwµçQÉ¥¹œÉ•ÅÕ•ÍÑ•‘9…µ”°%M•ÐñÍÑÉ¥¹œøÕÍ•‘9…µ•Ì¤4(€€€ì4(€€€€€€€Ù…È¥¹Ù…±¥‘¡…É…Ñ•ÉÌ€ô¹•Ü!…Í¡M•Ðñ¡…Èø¡llœ°€tœ°€œèœ°€œ¨œ°€œüœ°€œ¼œ°€qpt¤ì4(€€€€€€€Ù…È±•…¹•€ô¹•ÜÍÑÉ¥¹œ¡É•ÅÕ•ÍÑ•‘9…µ”4(€€€€€€€€€€€€€€€€¹M•±•Ð¡¡…É…Ñ•È€ôø¥¹Ù…±¥‘¡…É…Ñ•ÉÌ¹½¹Ñ…¥¹Ì¡¡…É…Ñ•È¤€ü€œ€œ€è¡…É…Ñ•È¤4(€€€€€€€€€€€€€€€€¹Q½ÉÉ…ä ¤¤4(€€€€€€€€€€€€¹QÉ¥´ ¤4(€€€€€€€€€€€€¹QÉ¥´ pœœ¤4(€€€€€€€€€€€€¹QÉ¥´ ¤ì4(€€€€€€€±•…¹•€ôÍÑÉ¥¹œ¹)½¥¸ ˆ€ˆ°±•…¹•¹MÁ±¥Ð œ€œ°MÑÉ¥¹MÁ±¥Ñ=ÁÑ¥½¹Ì¹I•µ½Ù•µÁÑå¹ÑÉ¥•Ì¤¤ì4(€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡±•…¹•¤¤±•…¹•€ô€‰1½…Ñ¥½¸ˆì4(4(€€€€€€€Ù…È‰…Í•9…µ”€ô±•…¹•‘l¸¹5…Ñ ¹5¥¸ ÌÄ°±•…¹•¹1•¹Ñ ¥tì4(€€€€€€€Ù…È…¹‘¥‘…Ñ”€ô‰…Í•9…µ”ì4(€€€€€€€Ù…ÈÍÕ™™¥á9Õµ‰•È€ô€Èì4(€€€€€€€Ý¡¥±”€ …ÕÍ•‘9…µ•Ì¹‘¡…¹‘¥‘…Ñ”¤¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÍÕ™™¥à€ô€ˆ€¡íÍÕ™™¥á9Õµ‰•È¬­ô¤ˆì4(€€€€€€€€€€€…¹‘¥‘…Ñ”€ô‰…Í•9…µ•l¸¹5…Ñ ¹5¥¸¡‰…Í•9…µ”¹1•¹Ñ °€ÌÄ€´ÍÕ™™¥à¹1•¹Ñ ¥t€¬ÍÕ™™¥àì4(€€€€€€€ô4(€€€€€€€É•ÑÕÉ¸…¹‘¥‘…Ñ”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œMÑ…ÑÕÍQ•áÐ¡9•ÑÝ½É­MÑ…Ñ”ÍÑ…Ñ”¤€ôøÍÑ…Ñ”ÍÝ¥Ñ 4(€€€ì4(€€€€€€€9•ÑÝ½É­MÑ…Ñ”¹I•…¡…‰±”€ôø€‰=¹±¥¹”ˆ°4(€€€€€€€9•ÑÝ½É­MÑ…Ñ”¹U¹É•…¡…‰±”€ôø€‰=™™±¥¹”ˆ°4(€€€€€€€9•ÑÝ½É­MÑ…Ñ”¹9½‘‘É•ÍÌ€ôø€‰9¼%@ˆ°4(€€€€€€€9•ÑÝ½É­MÑ…Ñ”¹A…ÉÑ¥…°€ôø€‰A…ÉÑ¥…±±ä½¹±¥¹”ˆ°4(€€€€€€€9•ÑÝ½É­MÑ…Ñ”¹5…5¥Íµ…Ñ €ôø€‰5µ¥Íµ…Ñ ˆ°4(€€€€€€€|€ôø€‰]…¥Ñ¥¹œÑ¼Ù•É¥™äˆ4(€€€ôì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ1½…±Q¥µ”¡…Ñ•Q¥µ”üÙ…±Õ”¤€ôøÙ…±Õ”ü¹Q½1½…±Q¥µ” ¤¹Q½MÑÉ¥¹œ ‰åååäµ54µ‘! éµ´éÍÌˆ¤€üüÍÑÉ¥¹œ¹µÁÑäì4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ%ÁM½ÉÑY…±Õ”¡ÍÑÉ¥¹œ¥À¤€ôøÍÑÉ¥¹œ¹)½¥¸ œ¸œ°¥À¹MÁ±¥Ð œ¸œ¤¹M•±•Ð¡Á…ÉÐ€ôø4(€€€€€€€¥¹Ð¹QÉåA…ÉÍ”¡Á…ÉÐ°½ÕÐÙ…ÈÙ…±Õ”¤€üÙ…±Õ”¹Q½MÑÉ¥¹œ ‰Ìˆ¤€èÁ…ÉÐ¤¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÙ½¥]É¥Ñ•Q•áÐ¡i¥ÁÉ¡¥Ù”…É¡¥Ù”°ÍÑÉ¥¹œÁ…Ñ °ÍÑÉ¥¹œ½¹Ñ•¹Ð¤4(€€€ì4(€€€€€€€Ù…È•¹ÑÉä€ô…É¡¥Ù”¹É•…Ñ•¹ÑÉä¡Á…Ñ °½µÁÉ•ÍÍ¥½¹1•Ù•°¹=ÁÑ¥µ…°¤ì4(€€€€€€€ÕÍ¥¹œÙ…ÈÍÑÉ•…´€ô•¹ÑÉä¹=Á•¸ ¤ì4(€€€€€€€ÕÍ¥¹œÙ…ÈÝÉ¥Ñ•È€ô¹•ÜMÑÉ•…µ]É¥Ñ•È¡ÍÑÉ•…´°¹•ÜUQá¹½‘¥¹œ¡™…±Í”¤¤ì4(€€€€€€€ÝÉ¥Ñ•È¹]É¥Ñ”¡½¹Ñ•¹Ð¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ½¹Ñ•¹ÑQåÁ•Íaµ°¡¥¹ÐÍ¡••Ñ½Õ¹Ð¤4(€€€ì4(€€€€€€€Ù…ÈÍ¡••Ñ=Ù•ÉÉ¥‘•Ì€ôÍÑÉ¥¹œ¹½¹…Ð¡¹Õµ•É…‰±”¹I…¹” Ä°Í¡••Ñ½Õ¹Ð¤¹M•±•Ð¡¥¹‘•à€ôø4(€€€€€€€€€€€€ˆñ=Ù•ÉÉ¥‘”A…ÉÑ9…µ”õpˆ½á°½Ý½É­Í¡••ÑÌ½Í¡••Ñí¥¹‘•áô¹áµ±pˆ½¹Ñ•¹ÑQåÁ”õp‰…ÁÁ±¥…Ñ¥½¸½Ù¹¹½Á•¹áµ±™½Éµ…ÑÌµ½™™¥•‘½Õµ•¹Ð¹ÍÁÉ•…‘Í¡••Ñµ°¹Ý½É­Í¡••Ð­áµ±pˆ¼øˆ¤¤ì4(€€€€€€€É•ÑÕÉ¸€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€€€€€€€€ˆñQåÁ•Ìáµ±¹Ìõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½Á…­…”¼ÈÀÀØ½½¹Ñ•¹ÐµÑåÁ•Ípˆøˆ€¬4(€€€€€€€€€€€€€€€ˆñ•™…Õ±ÐáÑ•¹Í¥½¸õp‰É•±Ípˆ½¹Ñ•¹ÑQåÁ”õp‰…ÁÁ±¥…Ñ¥½¸½Ù¹¹½Á•¹áµ±™½Éµ…ÑÌµÁ…­…”¹É•±…Ñ¥½¹Í¡¥ÁÌ­áµ±pˆ¼øˆ€¬4(€€€€€€€€€€€€€€€ˆñ•™…Õ±ÐáÑ•¹Í¥½¸õp‰áµ±pˆ½¹Ñ•¹ÑQåÁ”õp‰…ÁÁ±¥…Ñ¥½¸½áµ±pˆ¼øˆ€¬4(€€€€€€€€€€€€€€€ˆñ=Ù•ÉÉ¥‘”A…ÉÑ9…µ”õpˆ½á°½Ý½É­‰½½¬¹áµ±pˆ½¹Ñ•¹ÑQåÁ”õp‰…ÁÁ±¥…Ñ¥½¸½Ù¹¹½Á•¹áµ±™½Éµ…ÑÌµ½™™¥•‘½Õµ•¹Ð¹ÍÁÉ•…‘Í¡••Ñµ°¹Í¡••Ð¹µ…¥¸­áµ±pˆ¼øˆ€¬4(€€€€€€€€€€€€€€€ˆñ=Ù•ÉÉ¥‘”A…ÉÑ9…µ”õpˆ½á°½ÍÑå±•Ì¹áµ±pˆ½¹Ñ•¹ÑQåÁ”õp‰…ÁÁ±¥…Ñ¥½¸½Ù¹¹½Á•¹áµ±™½Éµ…ÑÌµ½™™¥•‘½Õµ•¹Ð¹ÍÁÉ•…‘Í¡••Ñµ°¹ÍÑå±•Ì­áµ±pˆ¼øˆ€¬4(€€€€€€€€€€€€€€€ˆñ=Ù•ÉÉ¥‘”A…ÉÑ9…µ”õpˆ½‘½AÉ½ÁÌ½½É”¹áµ±pˆ½¹Ñ•¹ÑQåÁ”õp‰…ÁÁ±¥…Ñ¥½¸½Ù¹¹½Á•¹áµ±™½Éµ…ÑÌµÁ…­…”¹½É”µÁÉ½Á•ÉÑ¥•Ì­áµ±pˆ¼øˆ€¬4(€€€€€€€€€€€€€€€ˆñ=Ù•ÉÉ¥‘”A…ÉÑ9…µ”õpˆ½‘½AÉ½ÁÌ½…ÁÀ¹áµ±pˆ½¹Ñ•¹ÑQåÁ”õp‰…ÁÁ±¥…Ñ¥½¸½Ù¹¹½Á•¹áµ±™½Éµ…ÑÌµ½™™¥•‘½Õµ•¹Ð¹•áÑ•¹‘•µÁÉ½Á•ÉÑ¥•Ì­áµ±pˆ¼øˆ€¬4(€€€€€€€€€€€€€€Í¡••Ñ=Ù•ÉÉ¥‘•Ì€¬€ˆð½QåÁ•Ìøˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œA…­…•I•±…Ñ¥½¹Í¡¥ÁÍaµ° ¤€ôø4(€€€€€€€€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€ˆñI•±…Ñ¥½¹Í¡¥ÁÌáµ±¹Ìõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½Á…­…”¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÍpˆøˆ€¬4(€€€€€€€€ˆñI•±…Ñ¥½¹Í¡¥À%õp‰É%ÅpˆQåÁ”õp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½½™™¥•½Õµ•¹Ð¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÌ½½™™¥•½Õµ•¹ÑpˆQ…É•Ðõp‰á°½Ý½É­‰½½¬¹áµ±pˆ¼øˆ€¬4(€€€€€€€€ˆñI•±…Ñ¥½¹Í¡¥À%õp‰É%ÉpˆQåÁ”õp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½Á…­…”¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÌ½µ•Ñ…‘…Ñ„½½É”µÁÉ½Á•ÉÑ¥•ÍpˆQ…É•Ðõp‰‘½AÉ½ÁÌ½½É”¹áµ±pˆ¼øˆ€¬4(€€€€€€€€ˆñI•±…Ñ¥½¹Í¡¥À%õp‰É%ÍpˆQåÁ”õp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½½™™¥•½Õµ•¹Ð¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÌ½•áÑ•¹‘•µÁÉ½Á•ÉÑ¥•ÍpˆQ…É•Ðõp‰‘½AÉ½ÁÌ½…ÁÀ¹áµ±pˆ¼øˆ€¬4(€€€€€€€€ˆð½I•±…Ñ¥½¹Í¡¥ÁÌøˆì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ]½É­‰½½­aµ°¡%I•…‘=¹±å1¥ÍÐñáÁ½ÉÑM¡••ÐøÍ¡••ÑÌ¤4(€€€ì4(€€€€€€€Ù…ÈÍ¡••Ñaµ°€ôÍÑÉ¥¹œ¹½¹…Ð¡Í¡••ÑÌ¹M•±•Ð ¡Í¡••Ð°¥¹‘•à¤€ôø4(€€€€€€€€€€€€ˆñÍ¡••Ð¹…µ”õp‰íÍ…Á•ÑÑÉ¥‰ÕÑ”¡Í¡••Ð¹9…µ”¥õpˆÍ¡••Ñ%õp‰í¥¹‘•à€¬€ÅõpˆÈé¥õp‰É%‘í¥¹‘•à€¬€Åõpˆ¼øˆ¤¤ì4(€€€€€€€É•ÑÕÉ¸€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€€€€€€€€ˆñÝ½É­‰½½¬áµ±¹Ìõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½ÍÁÉ•…‘Í¡••Ñµ°¼ÈÀÀØ½µ…¥¹pˆáµ±¹ÌéÈõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½½™™¥•½Õµ•¹Ð¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÍpˆøˆ€¬4(€€€€€€€€€€€€€€€ˆñÍ¡••ÑÌùíÍ¡••Ñaµ±ôð½Í¡••ÑÌøð½Ý½É­‰½½¬øˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ]½É­‰½½­I•±…Ñ¥½¹Í¡¥ÁÍaµ°¡¥¹ÐÍ¡••Ñ½Õ¹Ð¤4(€€€ì4(€€€€€€€Ù…ÈÉ•±…Ñ¥½¹Í¡¥ÁÌ€ôÍÑÉ¥¹œ¹½¹…Ð¡¹Õµ•É…‰±”¹I…¹” Ä°Í¡••Ñ½Õ¹Ð¤¹M•±•Ð¡¥¹‘•à€ôø4(€€€€€€€€€€€€ˆñI•±…Ñ¥½¹Í¡¥À%õp‰É%‘í¥¹‘•áõpˆQåÁ”õp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½½™™¥•½Õµ•¹Ð¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÌ½Ý½É­Í¡••ÑpˆQ…É•Ðõp‰Ý½É­Í¡••ÑÌ½Í¡••Ñí¥¹‘•áô¹áµ±pˆ¼øˆ¤¤ì4(€€€€€€€É•±…Ñ¥½¹Í¡¥ÁÌ€¬ô€ˆñI•±…Ñ¥½¹Í¡¥À%õp‰É%‘íÍ¡••Ñ½Õ¹Ð€¬€ÅõpˆQåÁ”õp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½½™™¥•½Õµ•¹Ð¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÌ½ÍÑå±•ÍpˆQ…É•Ðõp‰ÍÑå±•Ì¹áµ±pˆ¼øˆì4(€€€€€€€É•ÑÕÉ¸€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€€€€€€€€ˆñI•±…Ñ¥½¹Í¡¥ÁÌáµ±¹Ìõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½Á…­…”¼ÈÀÀØ½É•±…Ñ¥½¹Í¡¥ÁÍpˆùíÉ•±…Ñ¥½¹Í¡¥ÁÍôð½I•±…Ñ¥½¹Í¡¥ÁÌøˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œMÑå±•Íaµ° ¤€ôø4(€€€€€€€€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€ˆñÍÑå±•M¡••Ðáµ±¹Ìõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½ÍÁÉ•…‘Í¡••Ñµ°¼ÈÀÀØ½µ…¥¹pˆøˆ€¬4(€€€€€€€€ˆñ™½¹ÑÌ½Õ¹ÐõpˆÍpˆøˆ€¬4(€€€€€€€€ˆñ™½¹ÐøñÍèÙ…°õpˆÄÁpˆ¼øñ¹…µ”Ù…°õp‰ÁÑ½Ípˆ¼øñ™…µ¥±äÙ…°õpˆÉpˆ¼øð½™½¹Ðøˆ€¬4(€€€€€€€€ˆñ™½¹Ðøñˆ¼øñ½±½ÈÉˆõp‰pˆ¼øñÍèÙ…°õpˆÄÙpˆ¼øñ¹…µ”Ù…°õp‰ÁÑ½Ì¥ÍÁ±…åpˆ¼øð½™½¹Ðøˆ€¬4(€€€€€€€€ˆñ™½¹Ðøñˆ¼øñ½±½ÈÉˆõp‰pˆ¼øñÍèÙ…°õpˆÄÁpˆ¼øñ¹…µ”Ù…°õp‰ÁÑ½Ípˆ¼øð½™½¹Ðøˆ€¬4(€€€€€€€€ˆð½™½¹ÑÌøˆ€¬4(€€€€€€€€ˆñ™¥±±Ì½Õ¹ÐõpˆÑpˆøñ™¥±°øñÁ…ÑÑ•É¹¥±°Á…ÑÑ•É¹QåÁ”õp‰¹½¹•pˆ¼øð½™¥±°øñ™¥±°øñÁ…ÑÑ•É¹¥±°Á…ÑÑ•É¹QåÁ”õp‰É…äÄÈÕpˆ¼øð½™¥±°øˆ€¬4(€€€€€€€€ˆñ™¥±°øñÁ…ÑÑ•É¹¥±°Á…ÑÑ•É¹QåÁ”õp‰Í½±¥‘pˆøñ™½±½ÈÉˆõp‰ÁÄÜÉpˆ¼øñ‰½±½È¥¹‘•á•õpˆØÑpˆ¼øð½Á…ÑÑ•É¹¥±°øð½™¥±°øˆ€¬4(€€€€€€€€ˆñ™¥±°øñÁ…ÑÑ•É¹¥±°Á…ÑÑ•É¹QåÁ”õp‰Í½±¥‘pˆøñ™½±½ÈÉˆõp‰ÈÔØÍ	pˆ¼øñ‰½±½È¥¹‘•á•õpˆØÑpˆ¼øð½Á…ÑÑ•É¹¥±°øð½™¥±°øð½™¥±±Ìøˆ€¬4(€€€€€€€€ˆñ‰½É‘•ÉÌ½Õ¹ÐõpˆÉpˆøñ‰½É‘•Èøñ±•™Ð¼øñÉ¥¡Ð¼øñÑ½À¼øñ‰½ÑÑ½´¼øñ‘¥…½¹…°¼øð½‰½É‘•Èøˆ€¬4(€€€€€€€€ˆñ‰½É‘•Èøñ±•™ÐÍÑå±”õp‰Ñ¡¥¹pˆøñ½±½ÈÉˆõp‰Ípˆ¼øð½±•™ÐøñÉ¥¡ÐÍÑå±”õp‰Ñ¡¥¹pˆøñ½±½ÈÉˆõp‰Ípˆ¼øð½É¥¡Ðøˆ€¬4(€€€€€€€€ˆñÑ½ÀÍÑå±”õp‰Ñ¡¥¹pˆøñ½±½ÈÉˆõp‰Ípˆ¼øð½Ñ½Àøñ‰½ÑÑ½´ÍÑå±”õp‰Ñ¡¥¹pˆøñ½±½ÈÉˆõp‰Ípˆ¼øð½‰½ÑÑ½´øñ‘¥…½¹…°¼øð½‰½É‘•Èøð½‰½É‘•ÉÌøˆ€¬4(€€€€€€€€ˆñ•±±MÑå±•a™Ì½Õ¹ÐõpˆÅpˆøñá˜¹ÕµµÑ%õpˆÁpˆ™½¹Ñ%õpˆÁpˆ™¥±±%õpˆÁpˆ‰½É‘•É%õpˆÁpˆ¼øð½•±±MÑå±•a™Ìøˆ€¬4(€€€€€€€€ˆñ•±±a™Ì½Õ¹ÐõpˆÑpˆøˆ€¬4(€€€€€€€€ˆñá˜¹ÕµµÑ%õpˆÁpˆ™½¹Ñ%õpˆÁpˆ™¥±±%õpˆÁpˆ‰½É‘•É%õpˆÁpˆá™%õpˆÁpˆ¼øˆ€¬4(€€€€€€€€ˆñá˜¹ÕµµÑ%õpˆÁpˆ™½¹Ñ%õpˆÅpˆ™¥±±%õpˆÉpˆ‰½É‘•É%õpˆÁpˆá™%õpˆÁpˆ…ÁÁ±å½¹ÐõpˆÅpˆ…ÁÁ±å¥±°õpˆÅpˆøñ…±¥¹µ•¹ÐÙ•ÉÑ¥…°õp‰•¹Ñ•Épˆ¼øð½á˜øˆ€¬4(€€€€€€€€ˆñá˜¹ÕµµÑ%õpˆÁpˆ™½¹Ñ%õpˆÉpˆ™¥±±%õpˆÍpˆ‰½É‘•É%õpˆÅpˆá™%õpˆÁpˆ…ÁÁ±å½¹ÐõpˆÅpˆ…ÁÁ±å¥±°õpˆÅpˆ…ÁÁ±å	½É‘•ÈõpˆÅpˆøñ…±¥¹µ•¹ÐÙ•ÉÑ¥…°õp‰•¹Ñ•ÉpˆÝÉ…ÁQ•áÐõpˆÅpˆ¼øð½á˜øˆ€¬4(€€€€€€€€ˆñá˜¹ÕµµÑ%õpˆÁpˆ™½¹Ñ%õpˆÁpˆ™¥±±%õpˆÁpˆ‰½É‘•É%õpˆÅpˆá™%õpˆÁpˆ…ÁÁ±å	½É‘•ÈõpˆÅpˆøñ…±¥¹µ•¹ÐÙ•ÉÑ¥…°õp‰Ñ½ÁpˆÝÉ…ÁQ•áÐõpˆÅpˆ¼øð½á˜øˆ€¬4(€€€€€€€€ˆð½•±±a™Ìøñ•±±MÑå±•Ì½Õ¹ÐõpˆÅpˆøñ•±±MÑå±”¹…µ”õp‰9½Éµ…±pˆá™%õpˆÁpˆ‰Õ¥±Ñ¥¹%õpˆÁpˆ¼øð½•±±MÑå±•Ìøˆ€¬4(€€€€€€€€ˆð½ÍÑå±•M¡••Ðøˆì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ]½É­Í¡••Ñaµ°¡áÁ½ÉÑM¡••ÐÍ¡••Ð¤4(€€€ì4(€€€€€€€Ù…Èµ…á½±Õµ¸€ô5…Ñ ¹5…à Ä°Í¡••Ð¹I½ÝÌ¹5…à¡É½Ü€ôøÉ½Ü¹Y…±Õ•Ì¹½Õ¹Ð¤¤ì4(€€€€€€€Ù…È‘¥µ•¹Í¥½¸€ô€‰Äéí½±Õµ¹9…µ”¡µ…á½±Õµ¸€´€Ä¥õí5…Ñ ¹5…à Ä°Í¡••Ð¹I½ÝÌ¹½Õ¹Ð¥ôˆì4(€€€€€€€Ù…È½±Õµ¹Ì€ôÍÑÉ¥¹œ¹½¹…Ð¡Í¡••Ð¹½±Õµ¹]¥‘Ñ¡Ì¹M•±•Ð ¡Ý¥‘Ñ °¥¹‘•à¤€ôø4(€€€€€€€€€€€€ˆñ½°µ¥¸õp‰í¥¹‘•à€¬€Åõpˆµ…àõp‰í¥¹‘•à€¬€ÅõpˆÝ¥‘Ñ õp‰íÝ¥‘Ñ ¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¥õpˆÕÍÑ½µ]¥‘Ñ õpˆÅpˆ¼øˆ¤¤ì4(€€€€€€€Ù…ÈÉ½Ýaµ°€ô¹•ÜMÑÉ¥¹	Õ¥±‘•È ¤ì4(€€€€€€€™½È€¡Ù…ÈÉ½Ý%¹‘•à€ô€ÀìÉ½Ý%¹‘•à€ðÍ¡••Ð¹I½ÝÌ¹½Õ¹ÐìÉ½Ý%¹‘•à¬¬¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÉ½Ü€ôÍ¡••Ð¹I½ÝÍmÉ½Ý%¹‘•átì4(€€€€€€€€€€€Ù…ÈÉ½Ý9Õµ‰•È€ôÉ½Ý%¹‘•à€¬€Äì4(€€€€€€€€€€€Ù…È¡•¥¡Ð€ôI½Ý!•¥¡Ð¡Í¡••Ð°É½Ü¤ì4(€€€€€€€€€€€É½Ýaµ°¹ÁÁ•¹ ˆñÉ½ÜÈõp‰íÉ½Ý9Õµ‰•Éõpˆ¡Ðõp‰í¡•¥¡ÑõpˆÕÍÑ½µ!•¥¡ÐõpˆÅpˆøˆ¤ì4(€€€€€€€€€€€™½È€¡Ù…È½±Õµ¹%¹‘•à€ô€Àì½±Õµ¹%¹‘•à€ðÉ½Ü¹Y…±Õ•Ì¹½Õ¹Ðì½±Õµ¹%¹‘•à¬¬¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…È•±±I•™•É•¹”€ô€‰í½±Õµ¹9…µ”¡½±Õµ¹%¹‘•à¥õíÉ½Ý9Õµ‰•Éôˆì4(€€€€€€€€€€€€€€€Ù…ÈÙ…±Õ”€ôÉ½Ü¹Y…±Õ•Ím½±Õµ¹%¹‘•átì4(€€€€€€€€€€€€€€€¥˜€¡Ù…±Õ”¥Ì¹Õ±°ñðÙ…±Õ”¥ÌÍÑÉ¥¹œÑ•áÐ€˜˜Ñ•áÐ¹1•¹Ñ €ôô€À¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€É½Ýaµ°¹ÁÁ•¹ ˆñŒÈõp‰í•±±I•™•É•¹•õpˆÌõp‰íÉ½Ü¹MÑå±•õpˆ¼øˆ¤ì4(€€€€€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€€€€€¥˜€¡Ù…±Õ”¥Ì‰åÑ”½ÈÍ¡½ÉÐ½È¥¹Ð½È±½¹œ½È™±½…Ð½È‘½Õ‰±”½È‘•¥µ…°¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€É½Ýaµ°¹ÁÁ•¹ ˆñŒÈõp‰í•±±I•™•É•¹•õpˆÌõp‰íÉ½Ü¹MÑå±•õpˆÐõp‰¹pˆøñØùí½¹Ù•ÉÐ¹Q½MÑÉ¥¹œ¡Ù…±Õ”°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¥ôð½Øøð½Œøˆ¤ì4(€€€€€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€€€€€É½Ýaµ°¹ÁÁ•¹ ˆñŒÈõp‰í•±±I•™•É•¹•õpˆÌõp‰íÉ½Ü¹MÑå±•õpˆÐõp‰¥¹±¥¹•MÑÉpˆøñ¥ÌøñÐáµ°éÍÁ…”õp‰ÁÉ•Í•ÉÙ•pˆùíÍ…Á•Q•áÐ¡½¹Ù•ÉÐ¹Q½MÑÉ¥¹œ¡Ù…±Õ”°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€üüÍÑÉ¥¹œ¹µÁÑä¥ôð½Ðøð½¥Ìøð½Œøˆ¤ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€É½Ýaµ°¹ÁÁ•¹ ˆð½É½Üøˆ¤ì4(€€€€€€€ô4(4(€€€€€€€Ù…Èµ•É”€ôÍ¡••Ð¹I½ÝÌ¹½Õ¹Ð€ø€À€˜˜Í¡••Ð¹I½ÝÍlÁt¹Y…±Õ•Ì¹½Õ¹Ð€ø€Ä4(€€€€€€€€€€€€ü€ˆñµ•É••±±Ì½Õ¹ÐõpˆÅpˆøñµ•É••±°É•˜õp‰Äéí½±Õµ¹9…µ”¡µ…á½±Õµ¸€´€Ä¥ôÅpˆ¼øð½µ•É••±±Ìøˆ4(€€€€€€€€€€€€èÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€Ù…È™¥±Ñ•È€ôÍ¡••Ð¹ÕÑ½¥±Ñ•È€˜˜Í¡••Ð¹I½ÝÌ¹½Õ¹Ð€øôÍ¡••Ð¹!•…‘•ÉI½Ü4(€€€€€€€€€€€€ü€ˆñ…ÕÑ½¥±Ñ•ÈÉ•˜õp‰íÍ¡••Ð¹!•…‘•ÉI½Ýôéí½±Õµ¹9…µ”¡µ…á½±Õµ¸€´€Ä¥õíÍ¡••Ð¹I½ÝÌ¹½Õ¹Ñõpˆ¼øˆ4(€€€€€€€€€€€€èÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€Ù…ÈÁ…¹”€ôÍ¡••Ð¹!•…‘•ÉI½Ü€ø€À4(€€€€€€€€€€€€ü€ˆñÍ¡••ÑY¥•ÝÌøñÍ¡••ÑY¥•ÜÝ½É­‰½½­Y¥•Ý%õpˆÁpˆøñÁ…¹”åMÁ±¥Ðõp‰íÍ¡••Ð¹!•…‘•ÉI½ÝõpˆÑ½Á1•™Ñ•±°õp‰íÍ¡••Ð¹!•…‘•ÉI½Ü€¬€Åõpˆ…Ñ¥Ù•A…¹”õp‰‰½ÑÑ½µ1•™ÑpˆÍÑ…Ñ”õp‰™É½é•¹pˆ¼øð½Í¡••ÑY¥•Üøð½Í¡••ÑY¥•ÝÌøˆ4(€€€€€€€€€€€€è€ˆñÍ¡••ÑY¥•ÝÌøñÍ¡••ÑY¥•ÜÝ½É­‰½½­Y¥•Ý%õpˆÁpˆ¼øð½Í¡••ÑY¥•ÝÌøˆì4(€€€€€€€É•ÑÕÉ¸€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€€€€€€€€ˆñÝ½É­Í¡••Ðáµ±¹Ìõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½ÍÁÉ•…‘Í¡••Ñµ°¼ÈÀÀØ½µ…¥¹pˆøˆ€¬4(€€€€€€€€€€€€€€€ˆñ‘¥µ•¹Í¥½¸É•˜õp‰í‘¥µ•¹Í¥½¹õpˆ¼ùíÁ…¹•ôñÍ¡••Ñ½Éµ…ÑAÈ‘•™…Õ±ÑI½Ý!•¥¡ÐõpˆÄÕpˆ¼øñ½±Ìùí½±Õµ¹Íôð½½±Ìøˆ€¬4(€€€€€€€€€€€€€€€ˆñÍ¡••Ñ…Ñ„ùíÉ½Ýaµ±ôð½Í¡••Ñ…Ñ„ùí™¥±Ñ•Éõíµ•É•ôð½Ý½É­Í¡••Ðøˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ‘½Õ‰±”I½Ý!•¥¡Ð¡áÁ½ÉÑM¡••ÐÍ¡••Ð°áÁ½ÉÑI½ÜÉ½Ü¤4(€€€ì4(€€€€€€€¥˜€¡É½Ü¹MÑå±”€ôô€Ä¤É•ÑÕÉ¸€ÌÀì4(€€€€€€€¥˜€¡É½Ü¹MÑå±”€ôô€È¤É•ÑÕÉ¸€Èàì4(€€€€€€€¥˜€¡É½Ü¹MÑå±”€ôô€À¤É•ÑÕÉ¸€ÄÐì4(4(€€€€€€€Ù…ÈÉ•ÅÕ¥É•‘1¥¹•Ì€ô€Äì4(€€€€€€€™½È€¡Ù…È¥¹‘•à€ô€Àì¥¹‘•à€ðÉ½Ü¹Y…±Õ•Ì¹½Õ¹Ðì¥¹‘•à¬¬¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÑ•áÐ€ô½¹Ù•ÉÐ¹Q½MÑÉ¥¹œ¡É½Ü¹Y…±Õ•Ím¥¹‘•át°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤ì4(€€€€€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡Ñ•áÐ¤¤½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€Ù…ÈÝ¥‘Ñ €ô¥¹‘•à€ðÍ¡••Ð¹½±Õµ¹]¥‘Ñ¡Ì¹1•¹Ñ €üÍ¡••Ð¹½±Õµ¹]¥‘Ñ¡Ím¥¹‘•át€è€ÄÈì4(€€€€€€€€€€€Ù…È¡…É…Ñ•ÉÍA•É1¥¹”€ô5…Ñ ¹5…à à°€¡¥¹Ð¥5…Ñ ¹±½½È¡Ý¥‘Ñ €´€È¤¤ì4(€€€€€€€€€€€Ù…È•ÍÑ¥µ…Ñ•‘1¥¹•Ì€ôÑ•áÐ¹I•Á±…” ‰qÈˆ°ÍÑÉ¥¹œ¹µÁÑä¤4(€€€€€€€€€€€€€€€€¹MÁ±¥Ð q¸œ¤4(€€€€€€€€€€€€€€€€¹MÕ´¡±¥¹”€ôø5…Ñ ¹5…à Ä°€¡¥¹Ð¥5…Ñ ¹•¥±¥¹œ ¡‘½Õ‰±”¥±¥¹”¹1•¹Ñ €¼¡…É…Ñ•ÉÍA•É1¥¹”¤¤¤ì4(€€€€€€€€€€€É•ÅÕ¥É•‘1¥¹•Ì€ô5…Ñ ¹5…à¡É•ÅÕ¥É•‘1¥¹•Ì°•ÍÑ¥µ…Ñ•‘1¥¹•Ì¤ì4(€€€€€€€ô4(€€€€€€€É•ÑÕÉ¸5…Ñ ¹5¥¸ ÄÈÀ°€ÈÈ€¬€¡É•ÅÕ¥É•‘1¥¹•Ì€´€Ä¤€¨€ÄÔ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œÁÁAÉ½Á•ÉÑ¥•Íaµ°¡%I•…‘=¹±å1¥ÍÐñáÁ½ÉÑM¡••ÐøÍ¡••ÑÌ¤4(€€€ì4(€€€€€€€Ù…ÈÑ¥Ñ±•Ì€ôÍÑÉ¥¹œ¹½¹…Ð¡Í¡••ÑÌ¹M•±•Ð¡Í¡••Ð€ôø€ˆñÙÐé±ÁÍÑÈùíÍ…Á•Q•áÐ¡Í¡••Ð¹9…µ”¥ôð½ÙÐé±ÁÍÑÈøˆ¤¤ì4(€€€€€€€É•ÑÕÉ¸€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€€€€€€€€ˆñAÉ½Á•ÉÑ¥•Ìáµ±¹Ìõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½½™™¥•½Õµ•¹Ð¼ÈÀÀØ½•áÑ•¹‘•µÁÉ½Á•ÉÑ¥•Ípˆáµ±¹ÌéÙÐõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½½™™¥•½Õµ•¹Ð¼ÈÀÀØ½‘½AÉ½ÁÍYQåÁ•Ípˆøˆ€¬4(€€€€€€€€€€€€€€€ˆñÁÁ±¥…Ñ¥½¸ù%¹9…ÍŒð½ÁÁ±¥…Ñ¥½¸øñ½M•ÕÉ¥ÑäøÀð½½M•ÕÉ¥ÑäøñM…±•É½Àù™…±Í”ð½M…±•É½Àøˆ€¬4(€€€€€€€€€€€€€€€ˆñ!•…‘¥¹A…¥ÉÌøñÙÐéÙ•Ñ½ÈÍ¥é”õpˆÉpˆ‰…Í•QåÁ”õp‰Ù…É¥…¹ÑpˆøñÙÐéÙ…É¥…¹ÐøñÙÐé±ÁÍÑÈù]½É­Í¡••ÑÌð½ÙÐé±ÁÍÑÈøð½ÙÐéÙ…É¥…¹ÐøñÙÐéÙ…É¥…¹ÐøñÙÐé¤ÐùíÍ¡••ÑÌ¹½Õ¹Ñôð½ÙÐé¤Ðøð½ÙÐéÙ…É¥…¹Ðøð½ÙÐéÙ•Ñ½Èøð½!•…‘¥¹A…¥ÉÌøˆ€¬4(€€€€€€€€€€€€€€€ˆñQ¥Ñ±•Í=™A…ÉÑÌøñÙÐéÙ•Ñ½ÈÍ¥é”õp‰íÍ¡••ÑÌ¹½Õ¹Ñõpˆ‰…Í•QåÁ”õp‰±ÁÍÑÉpˆùíÑ¥Ñ±•Íôð½ÙÐéÙ•Ñ½Èøð½Q¥Ñ±•Í=™A…ÉÑÌøˆ€¬4(€€€€€€€€€€€€€€€ˆñ½µÁ…¹äù%¹8à1…‰Ìð½½µÁ…¹äøñ1¥¹­ÍUÁQ½…Ñ”ù™…±Í”ð½1¥¹­ÍUÁQ½…Ñ”øñM¡…É•‘½Œù™…±Í”ð½M¡…É•‘½Œøñ!åÁ•É±¥¹­Í¡…¹•ù™…±Í”ð½!åÁ•É±¥¹­Í¡…¹•øñÁÁY•ÉÍ¥½¸øÄØ¸ÀÌÀÀð½ÁÁY•ÉÍ¥½¸øð½AÉ½Á•ÉÑ¥•Ìøˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ½É•AÉ½Á•ÉÑ¥•Íaµ°¡ÍÑÉ¥¹œÝ½É­‰½½­Q¥Ñ±”¤4(€€€ì4(€€€€€€€Ù…È¹½Ü€ô…Ñ•Q¥µ”¹UÑ9½Ü¹Q½MÑÉ¥¹œ ‰åååäµ54µ‘P! éµ´éÍÌhœˆ¤ì4(€€€€€€€É•ÑÕÉ¸€ˆðýáµ°Ù•ÉÍ¥½¸õpˆÄ¸Ápˆ•¹½‘¥¹œõp‰UQ´ápˆÍÑ…¹‘…±½¹”õp‰å•Ípˆüøˆ€¬4(€€€€€€€€€€€€€€€ˆñÀé½É•AÉ½Á•ÉÑ¥•Ìáµ±¹ÌéÀõp‰¡ÑÑÀè¼½Í¡•µ…Ì¹½Á•¹áµ±™½Éµ…ÑÌ¹½Éœ½Á…­…”¼ÈÀÀØ½µ•Ñ…‘…Ñ„½½É”µÁÉ½Á•ÉÑ¥•Ípˆáµ±¹Ìé‘Œõp‰¡ÑÑÀè¼½ÁÕÉ°¹½Éœ½‘Œ½•±•µ•¹ÑÌ¼Ä¸Ä½pˆáµ±¹Ìé‘Ñ•ÉµÌõp‰¡ÑÑÀè¼½ÁÕÉ°¹½Éœ½‘Œ½Ñ•ÉµÌ½pˆáµ±¹Ìé‘µ¥ÑåÁ”õp‰¡ÑÑÀè¼½ÁÕÉ°¹½Éœ½‘Œ½‘µ¥ÑåÁ”½pˆáµ±¹ÌéáÍ¤õp‰¡ÑÑÀè¼½ÝÝÜ¹ÜÌ¹½Éœ¼ÈÀÀÄ½a51M¡•µ„µ¥¹ÍÑ…¹•pˆøˆ€¬4(€€€€€€€€€€€€€€€ˆñ‘ŒéÑ¥Ñ±”ùíÍ…Á•Q•áÐ¡Ý½É­‰½½­Q¥Ñ±”¥ôð½‘ŒéÑ¥Ñ±”øñ‘ŒéÉ•…Ñ½Èù%¹8à1…‰Ìð½‘ŒéÉ•…Ñ½ÈøñÀé±…ÍÑ5½‘¥™¥•‘	äù%¹8à1…‰Ìð½Àé±…ÍÑ5½‘¥™¥•‘	äøñ‘Ñ•ÉµÌéÉ•…Ñ•áÍ¤éÑåÁ”õp‰‘Ñ•ÉµÌé\ÍQpˆùí¹½Ýôð½‘Ñ•ÉµÌéÉ•…Ñ•øñ‘Ñ•ÉµÌéµ½‘¥™¥•áÍ¤éÑåÁ”õp‰‘Ñ•ÉµÌé\ÍQpˆùí¹½Ýôð½‘Ñ•ÉµÌéµ½‘¥™¥•øð½Àé½É•AÉ½Á•ÉÑ¥•Ìøˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œÍ…Á•Q•áÐ¡ÍÑÉ¥¹œÙ…±Õ”¤€ôøMåÍÑ•´¹M•ÕÉ¥Ñä¹M•ÕÉ¥Ñå±•µ•¹Ð¹Í…Á”¡Ù…±Õ”¤€üüÍÑÉ¥¹œ¹µÁÑäì4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œÍ…Á•ÑÑÉ¥‰ÕÑ”¡ÍÑÉ¥¹œÙ…±Õ”¤€ôøÍ…Á•Q•áÐ¡Ù…±Õ”¤¹I•Á±…” ‰pˆˆ°€ˆ™ÅÕ½Ðìˆ¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ½±Õµ¹9…µ”¡¥¹Ðé•É½	…Í•‘%¹‘•à¤4(€€€ì4(€€€€€€€Ù…ÈÉ•ÍÕ±Ð€ôÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€Ù…È¥¹‘•à€ôé•É½	…Í•‘%¹‘•à€¬€Äì4(€€€€€€€Ý¡¥±”€¡¥¹‘•à€ø€À¤4(€€€€€€€ì4(€€€€€€€€€€€¥¹‘•à´´ì4(€€€€€€€€€€€É•ÍÕ±Ð€ô€¡¡…È¤ œ€¬¥¹‘•à€”€ÈØ¤€¬É•ÍÕ±Ðì4(€€€€€€€€€€€¥¹‘•à€¼ô€ÈØì4(€€€€€€€ô4(€€€€€€€É•ÑÕÉ¸É•ÍÕ±Ðì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½É]½É­‰½½­M¡••Ð¡ÍÑÉ¥¹œ9…µ”°ÍÑÉ¥¹œA…Ñ ¤ì4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½É]½É­Í¡••ÑI½Ü¡¥¹ÐI½Ý9Õµ‰•È°¥Ñ¥½¹…Éäñ¥¹Ð°ÍÑÉ¥¹œøY…±Õ•Ì¤ì4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½ÉáÁ½ÉÑM¡••Ð¡ÍÑÉ¥¹œ9…µ”°1¥ÍÐñáÁ½ÉÑI½ÜøI½ÝÌ°‘½Õ‰±•mt½±Õµ¹]¥‘Ñ¡Ì°¥¹Ð!•…‘•ÉI½Ü°‰½½°ÕÑ½¥±Ñ•È¤ì4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½ÉáÁ½ÉÑI½Ü¡%I•…‘=¹±å1¥ÍÐñ½‰©•ÐüøY…±Õ•Ì°¥¹ÐMÑå±”¤4(€€€ì4(€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒáÁ½ÉÑI½ÜQ¥Ñ±”¡ÍÑÉ¥¹œÑ¥Ñ±”°¥¹Ð½±Õµ¹Ì¤€ôø¹•Ü 4(€€€€€€€€€€€¹•Ü½‰©•ÐýmtìÑ¥Ñ±”ô¹½¹…Ð¡¹Õµ•É…‰±”¹I•Á•…Ðñ½‰©•Ðüø¡ÍÑÉ¥¹œ¹µÁÑä°5…Ñ ¹5…à À°½±Õµ¹Ì€´€Ä¤¤¤¹Q½1¥ÍÐ ¤°€Ä¤ì4(€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒáÁ½ÉÑI½Ü!•…‘•È¡Á…É…µÌ½‰©•ÐýmtÙ…±Õ•Ì¤€ôø¹•Ü¡Ù…±Õ•Ì°€È¤ì4(€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒáÁ½ÉÑI½Ü	½‘ä¡Á…É…µÌ½‰©•ÐýmtÙ…±Õ•Ì¤€ôø¹•Ü¡Ù…±Õ•Ì°€Ì¤ì4(€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒáÁ½ÉÑI½Ü	±…¹¬¡¥¹Ð½±Õµ¹Ì¤€ôø¹•Ü¡¹Õµ•É…‰±”¹I•Á•…Ðñ½‰©•Ðüø¡ÍÑÉ¥¹œ¹µÁÑä°½±Õµ¹Ì¤¹Q½1¥ÍÐ ¤°€À¤ì4(€€€ô4)ô4