namespace InNasc;

internal static class MasterCheckoutPolicy
{
    public static AppData PreserveProtectedClients(
        AppData local,
        AppData master,
        MasterAccessControl access,
        MasterSession? session)
    {
        var protectedIds = access.Checkouts
            .Select(checkout => checkout.ClientId)
            .ToHashSet();
        foreach (var clientId in local.Clients.Select(client => client.Id)
                     .Concat(master.Clients.Select(client => client.Id))
                     .Distinct()
                     .Where(clientId =>
                         !MasterAccessService.CanAccessClient(access, session, clientId)))
            protectedIds.Add(clientId);
        if (protectedIds.Count == 0) return Clone(local);

        var prepared = Clone(local);
        prepared.Clients.RemoveAll(client => protectedIds.Contains(client.Id));
        foreach (var client in master.Clients.Where(client => protectedIds.Contains(client.Id)))
            prepared.Clients.Add(ClientSubmatrixService.CloneClient(client));
        return prepared;
    }

    public static int CheckedOutClientCount(MasterAccessControl access) =>
        access.Checkouts.Select(checkout => checkout.ClientId).Distinct().Count();

    public static void EnsureChangedClientsCanBeWritten(
        AppData baseline,
        AppData local,
        MasterAccessControl access,
        MasterSession? session)
    {
        if (access.Checkouts.Count == 0) return;
        var baselineClients = baseline.Clients.ToDictionary(client => client.Id);
        var localClients = local.Clients.ToDictionary(client => client.Id);
        var changedIds = baselineClients.Keys
            .Concat(localClients.Keys)
            .Distinct()
            .Where(clientId => Changed(clientId, baselineClients, localClients))
            .ToHashSet();
        foreach (var checkout in access.Checkouts.Where(checkout => changedIds.Contains(checkout.ClientId)))
        {
            var ownsCheckout = session is not null &&
                checkout.UserId == session.UserId &&
                local.Settings.ActiveCheckoutToken == checkout.CheckoutToken;
            if (ownsCheckout) continue;
            var clientName = localClients.GetValueOrDefault(checkout.ClientId)?.Name ??
                baselineClients.GetValueOrDefault(checkout.ClientId)?.Name ??
                checkout.ClientId.ToString("N")[..8].ToUpperInvariant();
            throw new ClientLockedException(clientName, checkout);
        }
    }

    private static bool Changed(
        Guid clientId,
        IReadOnlyDictionary<Guid, ClientRecord> baseline,
        IReadOnlyDictionary<Guid, ClientRecord> local)
    {
        var hasBaseline = baseline.TryGetValue(clientId, out var before);
        var hasLocal = local.TryGetValue(clientId, out var after);
        if (hasBaseline != hasLocal) return true;
        if (!hasBaseline) return false;
        return !string.Equals(
            SyncContentFingerprint.ComputeClient(before!),
            SyncContentFingerprint.ComputeClient(after!),
            StringComparison.OrdinalIgnoreCase);
    }

    private static AppData Clone(AppData source)
    {
        var clone = new AppData
        {
            ProjectName = source.ProjectName,
            Clients = source.Clients.Select(ClientSubmatrixService.CloneClient).ToList(),
            MasterAccess = MasterAccessService.Clone(source.MasterAccess),
            Settings = source.Settings
        };
        DataStore.Normalize(clone);
        return clone;
    }
}

internal sealed class ClientLockedException : InvalidOperationException
{
    public string ClientName { get; }
    public ClientCheckoutRecord Checkout { get; }

    public ClientLockedException(string clientName, ClientCheckoutRecord checkout)
        : base($"{clientName} is checked out by {Holder(checkout)}.")
    {
        ClientName = clientName;
        Checkout = checkout;
    }

    private static string Holder(ClientCheckoutRecord checkout)
    {
        var person = string.IsNullOrWhiteSpace(checkout.DisplayName)
            ? checkout.Username
            : checkout.DisplayName;
        return string.IsNullOrWhiteSpace(checkout.MachineName)
            ? person
            : $"{person} on {checkout.MachineName}";
    }
}

internal sealed class CheckoutOwnershipLostException : InvalidOperationException
{
    public CheckoutOwnershipLostException(string message) : base(message)
    {
    }
}
