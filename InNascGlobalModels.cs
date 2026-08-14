namespace InNasc;

internal sealed class InNascGlobalEnvelope
{
    public string Format { get; set; } = "InNasc Global";
    public int FormatVersion { get; set; } = 1;
    public Guid CatalogId { get; set; } = Guid.NewGuid();
    public List<InNascGlobalAccessRecord> Accounts { get; set; } = [];
    public string PayloadBase64 { get; set; } = string.Empty;
}

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
    public string CompanyKeySaltBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialNonceBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialCiphertextBase64 { get; set; } = string.Empty;
    public string CompanyKeyCredentialTagBase64 { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class InNascGlobalCatalog
{
    public int FormatVersion { get; set; } = 1;
    public Guid CatalogId { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<InNascGlobalUserRecord> Users { get; set; } = [];
    public List<InNascCompanyRecord> Companies { get; set; } = [];
}

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
    public string FilePath { get; set; } = string.Empty;
    public string CompanyKeyBase64 { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
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
