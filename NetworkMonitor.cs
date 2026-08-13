using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace InNasc;

public static class NetworkAdapterService
{
    public static List<NetworkAdapterChoice> GetAvailableAdapters()
    {
        var choices = new List<NetworkAdapterChoice>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                     .Where(nic => nic.NetworkInterfaceType !=
                                   System.Net.NetworkInformation.NetworkInterfaceType.Loopback))
        {
            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses
                         .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                         .Where(address => !IPAddress.IsLoopback(address.Address)))
            {
                choices.Add(new NetworkAdapterChoice(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    address.Address.ToString(),
                    address.IPv4Mask?.ToString() ?? string.Empty));
            }
        }

        return choices
            .OrderBy(choice => choice.NicName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(choice => choice.Ipv4Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class NetworkMonitor
{
    private readonly SemaphoreSlim _parallelLimit = new(8);

    public async Task CheckAllAsync(
        IReadOnlyCollection<EquipmentRecord> equipment,
        string sourceIpv4,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var sourceMask = NetworkAdapterService.GetAvailableAdapters()
            .FirstOrDefault(adapter => string.Equals(
                adapter.Ipv4Address, sourceIpv4, StringComparison.OrdinalIgnoreCase))?.SubnetMask
            ?? string.Empty;
        var tasks = equipment.Select(item => CheckOneLimitedAsync(
            item,
            sourceIpv4,
            sourceMask,
            timeoutMilliseconds,
            cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task CheckOneLimitedAsync(
        EquipmentRecord equipment,
        string sourceIpv4,
        string sourceMask,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        await _parallelLimit.WaitAsync(cancellationToken);
        try
        {
            equipment.EnsureNetworkInterfaces();
            if (!Ipv4AddressText.TryParse(sourceIpv4, out var source, out _) ||
                source.AddressFamily != AddressFamily.InterNetwork)
            {
                foreach (var networkInterface in equipment.NetworkInterfaces)
                {
                    networkInterface.NetworkState = networkInterface.HasAddress
                        ? NetworkState.Unknown
                        : NetworkState.NoAddress;
                    networkInterface.LastCheckedUtc = DateTime.UtcNow;
                    networkInterface.LastNetworkError = "Choose a network adapter before verification.";
                }
                equipment.UpdateAggregateNetworkState();
                return;
            }

            var checks = equipment.NetworkInterfaces.Select(networkInterface => CheckInterfaceAsync(
                networkInterface, source, sourceMask, timeoutMilliseconds, cancellationToken));
            await Task.WhenAll(checks);
            equipment.SyncLegacyNetworkFields();
            equipment.UpdateAggregateNetworkState();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            foreach (var networkInterface in equipment.NetworkInterfaces)
            {
                if (!networkInterface.HasAddress) continue;
                networkInterface.NetworkState = NetworkState.Unreachable;
                networkInterface.LastCheckedUtc = DateTime.UtcNow;
                networkInterface.LastLatencyMs = null;
                networkInterface.LastNetworkError = exception.Message;
            }
            equipment.UpdateAggregateNetworkState();
        }
        finally
        {
            _parallelLimit.Release();
        }
    }

    private static async Task CheckInterfaceAsync(
        NetworkInterfaceRecord networkInterface,
        IPAddress source,
        string sourceMask,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        networkInterface.HttpPortOpen = false;
        networkInterface.HttpsPortOpen = false;
        networkInterface.ObservedMacAddress = string.Empty;
        networkInterface.MacVerificationMessage = string.Empty;
        networkInterface.LastCheckedUtc = DateTime.UtcNow;
        if (!networkInterface.HasAddress)
        {
            networkInterface.NetworkState = NetworkState.NoAddress;
            networkInterface.LastLatencyMs = null;
            networkInterface.LastNetworkError = string.Empty;
            return;
        }
        if (!Ipv4AddressText.TryParse(networkInterface.IpAddress,
                out var destination, out var normalizedDestination))
        {
            networkInterface.NetworkState = NetworkState.Unreachable;
            networkInterface.LastLatencyMs = null;
            networkInterface.LastNetworkError = "IP is not a valid IPv4 address.";
            return;
        }
        networkInterface.IpAddress = normalizedDestination;

        var pingTask = Task.Run(
            () => SourceBoundIcmp.Ping(source, destination, timeoutMilliseconds), cancellationToken);
        var httpTask = TcpPortProbe.IsOpenAsync(source, destination, 80, timeoutMilliseconds, cancellationToken);
        var httpsTask = TcpPortProbe.IsOpenAsync(source, destination, 443, timeoutMilliseconds, cancellationToken);
        await Task.WhenAll(pingTask, httpTask, httpsTask);
        var ping = await pingTask;
        networkInterface.HttpPortOpen = await httpTask;
        networkInterface.HttpsPortOpen = await httpsTask;
        networkInterface.NetworkState = ping.Success ? NetworkState.Reachable : NetworkState.Unreachable;
        networkInterface.LastLatencyMs = ping.Success ? ping.RoundtripMilliseconds : null;
        networkInterface.LastNetworkError = ping.Error;

        if (!ping.Success || string.IsNullOrWhiteSpace(networkInterface.MacAddress)) return;
        if (!IsSameSubnet(source, destination, sourceMask))
        {
            networkInterface.MacVerificationMessage =
                "MAC could not be verified because the device is outside the selected NIC's local subnet.";
            return;
        }

        var observed = LocalNeighborResolver.TryResolve(destination, source);
        if (string.IsNullOrWhiteSpace(observed))
        {
            networkInterface.MacVerificationMessage =
                "MAC could not be verified from the Windows neighbor table.";
            return;
        }
        networkInterface.ObservedMacAddress = observed;
        if (!MacAddressText.EqualsNormalized(networkInterface.MacAddress, observed))
        {
            networkInterface.NetworkState = NetworkState.MacMismatch;
            networkInterface.MacVerificationMessage =
                $"Expected {networkInterface.MacAddress}; detected {observed}.";
        }
        else
        {
            networkInterface.MacVerificationMessage = $"MAC verified: {observed}.";
        }
    }

    private static bool IsSameSubnet(IPAddress source, IPAddress destination, string maskText)
    {
        if (!Ipv4AddressText.TryParse(maskText, out var mask, out _)) return false;
        var sourceBytes = source.GetAddressBytes();
        var destinationBytes = destination.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        return Enumerable.Range(0, 4).All(index =>
            (sourceBytes[index] & maskBytes[index]) == (destinationBytes[index] & maskBytes[index]));
    }
}

public sealed record PingCheckResult(bool Success, long RoundtripMilliseconds, string Error);

internal static class SourceBoundIcmp
{
    private const uint IpSuccess = 0;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct IpOptionInformation
    {
        public byte Ttl;
        public byte Tos;
        public byte Flags;
        public byte OptionsSize;
        public IntPtr OptionsData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IcmpEchoReply
    {
        public uint Address;
        public uint Status;
        public uint RoundTripTime;
        public ushort DataSize;
        public ushort Reserved;
        public IntPtr Data;
        public IpOptionInformation Options;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern IntPtr IcmpCreateFile();

    [DllImport("iphlpapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IcmpCloseHandle(IntPtr icmpHandle);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint IcmpSendEcho2Ex(
        IntPtr icmpHandle,
        IntPtr eventHandle,
        IntPtr apcRoutine,
        IntPtr apcContext,
        uint sourceAddress,
        uint destinationAddress,
        byte[] requestData,
        ushort requestSize,
        ref IpOptionInformation requestOptions,
        IntPtr replyBuffer,
        uint replySize,
        uint timeout);

    public static PingCheckResult Ping(IPAddress source, IPAddress destination, int timeoutMilliseconds)
    {
        if (!OperatingSystem.IsWindows())
            return new PingCheckResult(false, 0, "NIC-bound ICMP monitoring requires Windows.");

        var handle = IcmpCreateFile();
        if (handle == IntPtr.Zero || handle == InvalidHandleValue)
            return new PingCheckResult(false, 0, $"Unable to create ICMP handle ({Marshal.GetLastWin32Error()}).");

        var payload = System.Text.Encoding.ASCII.GetBytes("InNasc");
        var options = new IpOptionInformation { Ttl = 64 };
        var replySize = (uint)(Marshal.SizeOf<IcmpEchoReply>() + payload.Length + 16);
        var replyBuffer = Marshal.AllocHGlobal((int)replySize);

        try
        {
            var replies = IcmpSendEcho2Ex(
                handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                ToNativeIpv4(source),
                ToNativeIpv4(destination),
                payload,
                (ushort)payload.Length,
                ref options,
                replyBuffer,
                replySize,
                (uint)Math.Max(250, timeoutMilliseconds));

            if (replies == 0)
                return new PingCheckResult(false, 0, $"No ICMP reply ({Marshal.GetLastWin32Error()}).");

            var reply = Marshal.PtrToStructure<IcmpEchoReply>(replyBuffer);
            return reply.Status == IpSuccess
                ? new PingCheckResult(true, reply.RoundTripTime, string.Empty)
                : new PingCheckResult(false, 0, $"ICMP status {reply.Status}.");
        }
        finally
        {
            Marshal.FreeHGlobal(replyBuffer);
            IcmpCloseHandle(handle);
        }
    }

    private static uint ToNativeIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return BitConverter.ToUInt32(bytes, 0);
    }
}

internal static class TcpPortProbe
{
    public static async Task<bool> IsOpenAsync(
        IPAddress source,
        IPAddress destination,
        int port,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(source, 0));
            await client.ConnectAsync(destination, port, cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(Math.Max(250, timeoutMilliseconds)), cancellationToken);
            return client.Connected;
        }
        catch (Exception exception) when (exception is SocketException or TimeoutException or OperationCanceledException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            return false;
        }
    }
}

internal static class LocalNeighborResolver
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(
        uint destinationIp,
        uint sourceIp,
        byte[] macAddress,
        ref int physicalAddressLength);

    public static string TryResolve(IPAddress destination, IPAddress source)
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        var buffer = new byte[6];
        var length = buffer.Length;
        var result = SendARP(ToNativeIpv4(destination), ToNativeIpv4(source), buffer, ref length);
        if (result != 0 || length != 6) return string.Empty;
        return string.Join(":", buffer.Take(length).Select(value => value.ToString("X2")));
    }

    private static uint ToNativeIpv4(IPAddress address) =>
        BitConverter.ToUInt32(address.GetAddressBytes(), 0);
}
