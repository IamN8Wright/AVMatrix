namespace InNasc;

internal sealed record CheckoutRecoveryResult(
    bool Preserved,
    string ClientName,
    string NewHolder);

internal static class CheckoutRecoveryService
{
    public static CheckoutRecoveryResult PreserveLostCheckout(
        AppData data,
        DataStore store,
        MasterAccessControl currentAccess)
    {
        var clientId = data.Settings.ActiveCheckoutClientId;
        if (!clientId.HasValue)
            return new CheckoutRecoveryResult(false, string.Empty, string.Empty);

        var localClient = data.Clients.FirstOrDefault(client => client.Id == clientId.Value);
        if (localClient is not null)
            data.Settings.RecoveredCheckoutClient =
                ClientSubmatrixService.CloneClient(localClient);

        var checkout = currentAccess.Checkouts.FirstOrDefault(item =>
            item.ClientId == clientId.Value);
        var holder = checkout is null
            ? "another user"
            : string.IsNullOrWhiteSpace(checkout.DisplayName)
                ? checkout.Username
                : checkout.DisplayName;
        data.Settings.RecoveredCheckoutHolder = holder;
        data.Settings.RecoveredCheckoutUtc = DateTime.UtcNow;
        data.MasterAccess = MasterAccessService.Clone(currentAccess);
        SharedSyncService.ClearActiveCheckout(data.Settings);
        store.Save(data);
        return new CheckoutRecoveryResult(
            localClient is not null,
            localClient?.Name ?? clientId.Value.ToString("N")[..8].ToUpperInvariant(),
            holder);
    }
}
