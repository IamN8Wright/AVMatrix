$ErrorActionPreference = 'Stop'

function Read-Source([string]$Path) {
    return [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..' $Path))
}

function Write-Source([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot '..' $Path), $Text, [Text.UTF8Encoding]::new($false))
}

function Replace-Required([string]$Path, [string]$Old, [string]$New) {
    $text = Read-Source $Path
    if (-not $text.Contains($Old)) {
        throw "Required migration marker was not found in $Path.`n--- marker ---`n$Old"
    }
    Write-Source $Path ($text.Replace($Old, $New))
}

function Replace-VisibleBrand([string]$Path) {
    $text = Read-Source $Path
    $text = $text.Replace('AV Matrix Studio', 'InNasc')
    $text = $text.Replace('AV Matrix', 'InNasc')
    Write-Source $Path $text
}

# Shared/file-share company transport: .nasc is primary; .avmatrix remains readable.
Replace-Required 'SharedSyncService.cs' @'
        if (!string.Equals(Path.GetExtension(fullPath), ".avmatrix", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The shared master must use the .avmatrix extension.");
'@ @'
        if (!InNascFileTypes.IsCompanyPath(fullPath))
            throw new InvalidDataException(
                "The company file must use .nasc. Legacy .avmatrix files remain readable for migration.");
'@

$text = Read-Source 'SharedSyncService.cs'
$text = $text.Replace('.avmatrix"', '.nasc"')
$text = $text.Replace('shared master', 'company file')
$text = $text.Replace('Shared master', 'Company file')
Write-Source 'SharedSyncService.cs' $text

# Google Drive company transport accepts both the new extension and legacy masters.
Replace-Required 'GoogleDriveSyncService.cs' @'
        if (!metadata.CanEdit || !metadata.Name.EndsWith(".avmatrix", StringComparison.OrdinalIgnoreCase))
'@ @'
        if (!metadata.CanEdit ||
            !(metadata.Name.EndsWith(".nasc", StringComparison.OrdinalIgnoreCase) ||
              metadata.Name.EndsWith(".avmatrix", StringComparison.OrdinalIgnoreCase)))
'@
Replace-Required 'GoogleDriveSyncService.cs' @'
            throw new InvalidDataException("The linked Google Drive file must use the .avmatrix extension.");
'@ @'
            throw new InvalidDataException(
                "The linked Google Drive company file must use .nasc. Legacy .avmatrix files remain readable for migration.");
'@
$text = Read-Source 'GoogleDriveSyncService.cs'
$text = $text.Replace('.avmatrix"', '.nasc"')
$text = $text.Replace('Google Drive master', 'Google Drive company file')
$text = $text.Replace('master before', 'company file before')
Write-Source 'GoogleDriveSyncService.cs' $text

# User-facing file dialogs and labels.
Replace-VisibleBrand 'SettingsForm.cs'
$text = Read-Source 'SettingsForm.cs'
$text = $text.Replace('Back up all data (.avmatrix)', 'Back up all data (.nasc)')
$text = $text.Replace('InNasc transfer (*.avmatrix)|*.avmatrix', 'InNasc company backup (*.nasc)|*.nasc')
$text = $text.Replace('DefaultExt = "avmatrix"', 'DefaultExt = "nasc"')
$text = $text.Replace('.avmatrix"', '.nasc"')
$text = $text.Replace('The .avmatrix backup is ready', 'The .nasc backup is ready')
$text = $text.Replace('InNasc backup (*.avmatrix)|*.avmatrix|All files (*.*)|*.*', 'InNasc backup (*.nasc)|*.nasc|Legacy AV Matrix (*.avmatrix)|*.avmatrix|All files (*.*)|*.*')
Write-Source 'SettingsForm.cs' $text

foreach ($path in @(
    'SharedSyncForm.cs',
    'GoogleDriveSyncForm.cs',
    'PasswordDialog.cs',
    'MasterWelcomeControl.cs',
    'MasterSessionContext.cs',
    'ClientCheckoutProgressForm.cs',
    'EquipmentEditorForm.cs',
    'NetworkMonitor.cs',
    'XlsxService.cs',
    'GitHubMasterStorageForm.cs',
    'GitHubMasterMirrorService.cs',
    'GitHubMasterStorageService.cs'
)) {
    Replace-VisibleBrand $path
}

# File dialogs in the legacy shared-sync screens now lead with .nasc while retaining legacy import.
$text = Read-Source 'SharedSyncForm.cs'
$text = $text.Replace('*.avmatrix)|*.avmatrix', '*.nasc)|*.nasc')
$text = $text.Replace('DefaultExt = "avmatrix"', 'DefaultExt = "nasc"')
$text = $text.Replace('.avmatrix"', '.nasc"')
Write-Source 'SharedSyncForm.cs' $text

$text = Read-Source 'GoogleDriveSyncForm.cs'
$text = $text.Replace('.avmatrix', '.nasc')
Write-Source 'GoogleDriveSyncForm.cs' $text

# Main shell visible brand and automatic entry into an already authenticated company session.
Replace-VisibleBrand 'MainForm.cs'
$text = Read-Source 'MainForm.cs'
$text = $text.Replace('Network inventory', 'Systems in context.')
$text = $text.Replace('About InNasc', 'About InNasc')
$text = $text.Replace(@'
        ShowMasterWelcomePage();
        RefreshSyncIndicator();
'@, @'
        if (MasterSessionContext.Current is null)
            ShowMasterWelcomePage();
        else
            ShowWelcomePage();
        RefreshSyncIndicator();
'@)
Write-Source 'MainForm.cs' $text

# Default project identity for newly constructed data objects.
$text = Read-Source 'Models.cs'
$text = $text.Replace('public string ProjectName { get; set; } = "AV Matrix Studio";', 'public string ProjectName { get; set; } = "InNasc";')
Write-Source 'Models.cs' $text

Write-Host 'Targeted InNasc source migration applied.'
