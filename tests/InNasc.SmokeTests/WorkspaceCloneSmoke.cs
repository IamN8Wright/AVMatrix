using System.Runtime.CompilerServices;

namespace InNasc.SmokeTests;

internal static class WorkspaceCloneSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var source = new RoomRecord
        {
            Name = "Conference A",
            Notes = "Template room",
            Equipment =
            [
                new EquipmentRecord
                {
                    Description = "DSP",
                    Manufacturer = "Example",
                    PartNumber = "DSP-1",
                    EquipmentId = "DSP-A",
                    Hostname = "dsp-room",
                    SerialNumber = "SERIAL-123",
                    Firmware = "1.2.3",
                    Subnet = "255.255.255.0",
                    Gateway = "192.168.50.1",
                    SerialConnection = "RS-232",
                    Username = "admin",
                    Password = "secret",
                    Notes = "Same addressing is intentional",
                    SourceFile = "template.xlsx",
                    NetworkInterfaces =
                    [
                        new NetworkInterfaceRecord
                        {
                            Type = NetworkInterfaceType.Control,
                            IpAddress = "192.168.50.20",
                            MacAddress = "AA:BB:CC:DD:EE:FF",
                            NetworkState = NetworkState.Reachable,
                            LastCheckedUtc = DateTime.UtcNow,
                            LastLatencyMs = 1,
                            HttpPortOpen = true
                        }
                    ],
                    ConfigurationFiles =
                    [
                        new DeviceConfigurationFile
                        {
                            FileName = "dsp-config.bin",
                            ContentType = "application/octet-stream",
                            SizeBytes = 4,
                            Sha256 = "TEST",
                            ContentBase64 = Convert.ToBase64String([1, 2, 3, 4]),
                            ContentIncluded = true,
                            Notes = "Configuration",
                            AddedBy = "smoke-test"
                        }
                    ]
                }
            ]
        };

        var clone = WorkspaceCloneService.CloneRoom(source, "Conference B");
        Assert(clone.Id != source.Id, "Cloned room must receive a new internal ID.");
        Assert(clone.Name == "Conference B", "Cloned room name was not applied.");
        Assert(clone.Equipment.Count == 1, "Cloned room lost equipment.");

        var originalDevice = source.Equipment[0];
        var clonedDevice = clone.Equipment[0];
        Assert(clonedDevice.Id != originalDevice.Id, "Cloned equipment must receive a new internal ID.");
        Assert(clonedDevice.Hostname == originalDevice.Hostname, "Hostname was not preserved.");
        Assert(clonedDevice.SerialNumber == originalDevice.SerialNumber, "Serial number was not preserved.");
        Assert(clonedDevice.Username == originalDevice.Username && clonedDevice.Password == originalDevice.Password,
            "Device access fields were not preserved.");
        Assert(clonedDevice.NetworkInterfaces.Count == 1, "Network interface was not cloned.");
        Assert(clonedDevice.NetworkInterfaces[0].Id != originalDevice.NetworkInterfaces[0].Id,
            "Cloned network interface must receive a new internal ID.");
        Assert(clonedDevice.NetworkInterfaces[0].IpAddress == "192.168.50.20",
            "Intentional duplicate IP was not preserved.");
        Assert(clonedDevice.NetworkInterfaces[0].MacAddress == "AA:BB:CC:DD:EE:FF",
            "MAC address was not preserved.");
        Assert(clonedDevice.NetworkInterfaces[0].NetworkState == NetworkState.Unknown,
            "Cloned network verification state must reset to Waiting/Unknown.");
        Assert(clonedDevice.NetworkInterfaces[0].LastCheckedUtc is null,
            "Cloned network verification timestamp must reset.");
        Assert(!clonedDevice.NetworkInterfaces[0].HttpPortOpen,
            "Cloned portal detection state must reset.");

        Assert(clonedDevice.ConfigurationFiles.Count == 1, "Configuration file was not cloned.");
        Assert(clonedDevice.ConfigurationFiles[0].Id != originalDevice.ConfigurationFiles[0].Id,
            "Cloned configuration file must receive a new internal ID.");
        Assert(clonedDevice.ConfigurationFiles[0].ContentBase64 == originalDevice.ConfigurationFiles[0].ContentBase64,
            "Configuration-file payload was not preserved.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Workspace clone smoke test failed: " + message);
    }
}
