# SwiftBase

SwiftBase is a keyboard-first, dark-mode database workbench built with WPF and .NET 10. It connects only to databases you configure; no demo profiles, sample catalogs or default credentials are shipped.

## Highlights

- SQLite, PostgreSQL and MySQL providers with paged (500-row) lazy reads
- Searchable object explorer, multi-tab workspace, data/structure views
- Visual row filters, paging and sortable virtualized DataGrid
- Staged edits with color coding, SQL preview and transactional `Ctrl+S` saves
- Right-hand cell inspector (`Space`) with JSON pretty-printing
- SQL console with selection execution, autocomplete and cancellation
- CSV / JSON / INSERT export for query results
- Command palette (`Ctrl+P`), connection switcher (`Ctrl+K`) and connection URL import
- DPAPI-protected secrets, authenticated TLS for direct server connections, and fail-closed SSH host keys with explicit first-use trust

## Projects

| Path | Purpose |
| --- | --- |
| `src/SwiftBase.Core` | Providers, session/factory, SSH tunnels, credential vault and shared models |
| `src/SwiftBase.App` | WPF shell, views, ViewModels, controls and dark theme |

## Build

Requirements: .NET SDK 10.0.403 or newer, Windows 10/11.

```powershell
dotnet build SwiftBase.slnx -c Release
```

Or use the helper script:

```powershell
./build.ps1
./build.ps1 -Publish    # publishes a self-contained win-x64 build to artifacts/SwiftBase
./build.ps1 -Installer  # builds the Inno Setup installer in artifacts/installer
```

A ready-to-run installer can also be built directly with Inno Setup 6:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\SwiftBase-Setup.iss"
```

Debug run:

```powershell
dotnet run --project src/SwiftBase.App/SwiftBase.App.csproj
```

The default debug executable is:

```text
src/SwiftBase.App/bin/Debug/net10.0-windows/win-x64/SwiftBase.exe
```

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+P` | Command palette |
| `Ctrl+K` | Switch connection |
| `Ctrl+N` | Stage a new row |
| `Ctrl+F` | Toggle the table filter |
| `Ctrl+S` | Save staged changes in one transaction |
| `Space` / `Enter` | Open the cell inspector |
| `Ctrl+T` | New SQL query tab |
| `Ctrl+Enter` | Run the selected SQL (or the whole editor when nothing is selected) |
| `Delete` | Mark the selected row for deletion |
| `F5` | Refresh data / schema |

## Local data

SwiftBase stores non-secret connection metadata and DPAPI-protected secrets under:

```text
%APPDATA%\SwiftBase\
```

- `connections.json` — saved connections; secrets are DPAPI-encrypted for the current Windows account. The vault is never overwritten after a load/decrypt failure.
- `ssh_known_hosts.json` — SSH host-key fingerprints; unknown keys require explicit first-use trust and changed keys fail closed
- `queries.json` — saved query history
- `crash.log` — sanitized, user-ACL-restricted crash diagnostics, rotated at 256 KB. The old `connections.json.bak` file is removed on the next save.

No default/demo data is generated or written to disk.
