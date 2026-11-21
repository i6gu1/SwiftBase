# SwiftBase

SwiftBase is a keyboard-first, dark-mode database workbench built with WPF and .NET 10. It ships with a live demo workspace so the UI can be explored without a database server.

## Highlights

- SQLite, PostgreSQL and MySQL providers with paged (500-row) lazy reads
- Searchable object explorer, multi-tab workspace, data/structure views
- Visual row filters, paging and sortable virtualized DataGrid
- Staged edits with color coding, SQL preview and transactional `Ctrl+S` saves
- Right-hand cell inspector (`Space`) with JSON pretty-printing
- SQL console with selection execution, autocomplete and cancellation
- CSV / JSON / INSERT export for query results
- Command palette (`Ctrl+P`), connection switcher (`Ctrl+K`) and connection URL import
- DPAPI-protected secrets, TLS-required server connections and SSH tunnels with trust-on-first-use host keys

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

- `connections.json` — saved connections; secrets are DPAPI-encrypted for the current Windows account
- `connections.json.bak` — backup written before each vault save
- `ssh_known_hosts.json` — SSH host-key fingerprints; a changed fingerprint fails closed
- `queries.json` — saved query history
- `crash.log` — unhandled exception diagnostics

Demo connections and the 2,000-row sample catalog are generated in memory and are never written to the vault.
