from pathlib import Path

p = Path("tests/InNasc.SmokeTests/Program.cs")
s = p.read_text(encoding="utf-8")
old = '''        room.Equipment.Add(new EquipmentRecord { Description = "Two" });
        RequireThrows<DeviceLimitExceededException>(() =>
            PortableDataService.ExportMaster(companyPath, data, session));'''
new = '''        room.Equipment.Add(new EquipmentRecord { Description = "Two" });
        RequireThrows<DeviceLimitExceededException>(() =>
            DeviceLimitPolicy.RequireNewClientAllowed(data.MasterAccess, data));
        // Existing data remains editable/saveable when a license is over its limit;
        // only creation of new client cards/devices is locked.
        PortableDataService.ExportMaster(companyPath, data, session);'''
if old not in s:
    raise SystemExit("Missing over-limit save smoke-test anchor")
s = s.replace(old, new, 1)
p.write_text(s, encoding="utf-8")
