namespace InNasc;

internal sealed class InNascGlobalEnvelope
{
    public string Format { get; set; } = "InNasc Global";
    public int FormatVersion { get; set; } = 1;
    public Guid CatalogId { get; set; } = Guid.NewGuid();
    public List<InNascGlobalAccessRecord> Accounts { get; set; } = [];
    public string PayloadBase64 { get; set; } = string.Empty;
}

// Only Global Admin credentials live in the unencrypted envelope. Company-user
// credentials are stored inside the encrypted catalog and are never able to
// unwrap the Global catalog key.
internal sealed class InNascGlobalAccessRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordSaltBase64 { get; set; } = string.Empty;
    public string PasswordHashBase64 { get; set; } = string.Empty;
    public int PasswordIterations { get; set; } = 310000;
    public string GlobalKeySaltBase64 { get; set; } = string.Empty;
    public string GlobalKeyNonceBase64 { get; set; } = string.Empty;
    public string GlobalKeyCiphertextBase64 { get; set; } = string.Empty;
    public string GlobalKeyTagBase64 { get; set; } = string.Empty;

    // 5.1 compatibility fields. They are copied into company-scoped users once,
    // then ignored for all new Global Admin accounts.
    public string CompanyKeySaltBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialNonceBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialCiphertextBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialTagBase64 { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class InNascGlobalCatalog
{
    public int FormatVersion { get; set; } = 2;
    public Guid CatalogId { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<InNascGlobalAdminRecord> GlobalAdmins { get; set; } = [];
    public List<InNascCompanyRecord> Companies { get; set; } = [];

    // 5.1 compatibility. Upgraded catalogs move these records into GlobalAdmins
    // and the appropriate company Users collection, then clear this list.
    public List<InNascGlobalUserRecord> Users { get; set; } = [];
}

internal sealed class InNascGlobalAdminRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

// Legacy 5.1 profile retained only for automatic migration.
internal sealed class InNascGlobalUserRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsGlobalAdmin { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<InNascCompanyMembership> Companies { get; set; } = [];
}

internal sealed class InNascCompanyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string LogoBase64 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<InNascCompanyFileRecord> Files { get; set; } = [];
    public List<InNascCompanyUserRecord> Users { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // 5.1 compatibility fields. They become the first Files entry on upgrade.
    public string FilePath { get; set; } = string.Empty;
    public string CompanyKeyBase64 { get; set; } = string.Empty;
}

internal sealed class InNascCompanyFileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string CompanyKeyBase64 { get; set; } = string.Empty;

    // Zero means Unlimited. Positive values are hard caps on equipment records.
    public int DeviceLimit { get; set; } = 250;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class InNascCompanyUserRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MasterUserRole Role { get; set; } = MasterUserRole.Tech;
    public string PasswordSaltBase64 { get; set; } = string.Empty;
    public string PasswordHashBase64 { get; set; } = string.Empty;
    public int PasswordIterations { get; set; } = 310000;
    public string CompanyKeySaltBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialNonceBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialCiphertextBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialTagBase64 { get; set; } = string.Empty;
    public bool HasAllClientAccess { get; set; } = true;
    public List<Guid> ClientAccessIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public bool CredentialReady =>
        !string.IsNullOrWhiteSpace(PasswordSaltBase64) &&
        !string.IsNullOrWhiteSpace(PasswordHashBase64) &&
        !string.IsNullOrWhiteSpace(CompanyKeySaltBase64) &&
        !string.IsNullOrWhiteSpace(CompanyKeyCredentialNonceBase64) &&
        !string.IsNullOrWhiteSpace(CompanyKeyCredentialCiphertextBase64) &&
        !string.IsNullOrWhiteSpace(CompanyKeyCredentialTagBase64);
}

internal sealed class InNascCompanyMembership
{
    public Guid CompanyId { get; set; }
    public MasterUserRole Role { get; set; } = MasterUserRole.Tech;
    public bool HasAllClientAccess { get; set; } = true;
    public List<Guid> ClientAccessIds { get; set; } = [];
}

internal sealed record InNascGlobalSession(
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsGlobalAdmin,
    string GlobalKey);

internal sealed record InNascGlobalAdminSelection(
    string GlobalPath,
    InNascGlobalSession Session,
    InNascGlobalCatalog Catalog);
