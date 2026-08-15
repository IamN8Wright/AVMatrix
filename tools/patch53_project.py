from pathlib import Path

p = Path("InNasc.csproj")
s = p.read_text(encoding="utf-8")
anchor = '    <Compile Remove="InNascMembershipForm.cs" />\n'
addition = (
    '    <Compile Remove="InNascMembershipForm.cs" />\n'
    '    <Compile Remove="InNascLicenseSyncContract.cs" />\n'
    '    <Compile Remove="InNascLicenseAdminForms.cs" />\n'
    '    <Compile Remove="GlobalAdminUpdateService.cs" />\n'
)
if anchor not in s:
    raise SystemExit("Missing InNasc.csproj global-admin exclusion anchor")
s = s.replace(anchor, addition, 1)
p.write_text(s, encoding="utf-8")
