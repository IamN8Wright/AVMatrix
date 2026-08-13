# AV Matrix Studio 1.1.0

Created by InN8 Labs — 2026

## Client-facing Excel redesign

- The suggested `.xlsx` filename is the client name.
- The workbook metadata title and Project Summary A1 banner are the client name.
- Project Summary shows client address and notes, location and room counts, equipment count, and a complete location/room listing.
- The internal project name and network-verification statistics were removed from Project Summary.
- Every client location receives a separate worksheet with equipment grouped by room.
- Device sheets contain only Room, Description, Manufacturer, Hostname, Serial Number, Firmware, Primary IP, Secondary IPs, MAC Addresses, Subnet, Gateway, User Name, Password, and Notes.
- Secondary IPs retain their interface type, and MAC addresses include their type and associated IP.
- Legacy Target IP and other internal/status/audit columns are no longer exported.
- Long and multi-line values automatically receive enough row height to remain readable.
- Location names are safely converted into unique Excel worksheet names, including duplicate, long, or invalid names.

All Google Drive, password-protected JWE, verification, container movement, and import features from revisions 1.0.0 and 1.0.1 remain included.
