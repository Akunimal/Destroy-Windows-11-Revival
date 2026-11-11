# DWS 11 Enterprise v2.1.0 - Modern Architecture

Modern, high-performance system baseline enforcement and telemetry optimizer for **Windows 10 22H2+** and **Windows 11 (23H2 / 24H2+)**.

Built with **.NET 8 (LTS)**, **C# 12**, **Self-Contained Single-File GUI**, **Avalonia UI 11.2** (Fluent Design & Mica), **KTM Transacted Registry Engine**, and background **Named Pipe IPC Broker**.

---

## 🏗️ Architecture Overview

The solution `DWS.Enterprise.sln` is organized into three decoupled layers:

```
DWS.Enterprise.sln
├── src/
│   ├── DWS.Core/         # Enterprise Kernel logic (.NET 8 Windows, Embedded Rules)
│   │   ├── KTM/          # Kernel Transaction Manager (ktmw32.dll & advapi32.dll)
│   │   ├── Storage/      # Isolated RegLoadAppKey offline hives (Default NTUSER.DAT)
│   │   ├── Security/     # SystemPrivilegeBroker & BitLockerGuard (TPM PCR guard)
│   │   ├── Policy/       # Binary Group Policy generator (registry.pol PReg1 + gpupdate)
│   │   ├── Rules/        # SecureRuleEngine with RSA-4096 signature verification (37 embedded rules)
│   │   ├── Logging/      # TransactionJournal in %ProgramData%\DWS\journal
│   │   └── Json/         # DwsJsonContext (System.Text.Json Source Generator for AOT compatibility)
│   ├── DWS.GUI/          # Self-Contained Single-File GUI (Avalonia 11.2, requireAdministrator, /SILENT-AUDIT)
│   └── DWS.Worker/       # Privileged background service with Named Pipe IPC (\\.\pipe\DWS11_Enterprise_IPC_v2)
├── rules/
│   ├── rules.json        # Enterprise signed optimization policy rules (37 rules)
│   └── rules.json.sig    # RSA-4096 digital signature
└── scripts/
    ├── build.cmd         # One-click restore & release compilation
    ├── publish.cmd       # Self-contained single-file publisher producing dist\DWS11-Enterprise-v*-win-x64.exe
    └── run-tests.cmd     # Test suite execution
```

---

## ⚡ Core Technical Pillars

1. **Kernel Transaction Manager (KTM):**
   - Transacted registry operations via `ktmw32!CreateTransaction` and `advapi32!RegCreateKeyTransactedW`.
   - Full ACID transaction integrity: all registry writes succeed or auto-rollback on cancellation/exception.

2. **Group Policy Binary Writer (LGPO):**
   - Writes directly to `System32\GroupPolicy\Machine\registry.pol` (`PReg1` binary format).
   - Prevents Windows Update from silently reverting configured policies.

3. **Isolated AppKey Offline Hive:**
   - Modifies `Users\Default\NTUSER.DAT` via `advapi32!RegLoadAppKey` in a private namespace without global registry pollution.
   - Ensures newly created user accounts inherit optimal settings from day one.

4. **Self-Contained Single-File GUI:**
   - True single-executable distribution (`DWS11-Enterprise-v2.1.0-win-x64.exe`) with 0 external DLLs required.
   - Built with `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true`.
   - Native SkiaSharp, HarfBuzzSharp and Angle graphics run seamlessly without manual unpacking.
   - Manifest configured with `requireAdministrator` for direct UAC elevation.

5. **Embedded & Signed Policy Catalog:**
   - All 37 enterprise rules and RSA-4096 signature are embedded directly in `DWS.Core`.
   - Falls back automatically to embedded catalog if no local `rules.json` exists.
   - External rule overrides are strictly verified against pinned RSA-4096 authority and rejected if tampered.

6. **Persistence Watchdog & /SILENT-AUDIT:**
   - Headless background watchdog mode: executes `/SILENT-AUDIT` without opening a GUI window.
   - Saves structured audit results to `%ProgramData%\DWS\audit\`.
   - Returns exit code `0` (compliant), `2` (pending changes/regressions detected), or `1` (error).
   - Scheduled via Task Scheduler under `NT AUTHORITY\SYSTEM` on Windows Update `Event ID 19`.

7. **Named Pipe IPC Broker:**
   - Inter-process communication via `\\.\pipe\DWS11_Enterprise_IPC_v2` with secure token authentication.
   - Graceful in-process fallback when Worker service is not active.

---

## 🚀 Building & Publishing

### Prerequisites
- Windows 10 22H2 (Build 19045+) or Windows 11 (Build 22000+)
- .NET 8.0 SDK (automatically configured in `%USERPROFILE%\.dotnet`)

### Build
Run `scripts\build.cmd` or:
```cmd
dotnet build DWS.Enterprise.sln -c Release
```

### Running Tests
Run `scripts\run-tests.cmd` or:
```cmd
dotnet test tests\DWS.Tests\DWS.Tests.csproj
```

### Single-File Publishing
Run `scripts\publish.cmd`. The standalone executable and its SHA-256 hash will be generated in:
```
dist\DWS11-Enterprise-v2.1.0-win-x64.exe
dist\DWS11-Enterprise-v2.1.0-win-x64.exe.sha256
```
