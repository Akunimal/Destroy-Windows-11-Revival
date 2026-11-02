# DWS 11 Enterprise v2.0 - Modern Architecture

Modern, high-performance system baseline enforcement and telemetry optimizer for **Windows 10 22H2+** and **Windows 11 (23H2 / 24H2+)**.

Built with **.NET 8 (LTS)**, **C# 12**, **Native AOT**, **Avalonia UI 11.2** with Fluent Design & Mica, and background **Kestrel Minimal APIs**.

---

## 🏗️ Architecture Overview

The solution `DWS.Enterprise.sln` is organized into three decoupled layers:

```
DWS.Enterprise.sln
├── src/
│   ├── DWS.Core/         # Enterprise Kernel logic (.NET 8 Windows, AOT)
│   │   ├── KTM/          # Kernel Transaction Manager (ktmw32.dll & advapi32.dll)
│   │   ├── Storage/      # Isolated RegLoadAppKey offline hives (Default NTUSER.DAT)
│   │   ├── Security/     # SystemPrivilegeBroker & BitLockerGuard (TPM PCR guard)
│   │   ├── Policy/       # Binary Group Policy generator (registry.pol PReg1 + gpupdate)
│   │   ├── Rules/        # SecureRuleEngine with RSA-4096 signature verification
│   │   ├── Logging/      # TransactionJournal with atomic .tmp file moves and category rollback
│   │   └── Json/         # DwsJsonContext (System.Text.Json Source Generator for Native AOT)
│   ├── DWS.Worker/       # SYSTEM background service with Kestrel Minimal APIs (localhost:5123)
│   └── DWS.GUI/          # Modern Desktop UI built with Avalonia 11.2 (Fluent, Mica, MVVM)
├── rules/
│   └── rules.json        # Enterprise signed optimization policy rules
└── scripts/
    ├── build.cmd         # One-click restore & release compilation
    ├── publish-aot.cmd   # Native AOT ahead-of-time publishing (win-x64)
    └── sign-rules.ps1    # RSA-4096 rule signature tool
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

4. **Zero-Reflection Native AOT:**
   - Source generated JSON via `DwsJsonContext` (`JsonSerializerContext`).
   - All P/Invoke calls modernized to `[LibraryImport]` with compile-time stubs.

5. **Persistence Watchdog:**
   - Automatically scheduled in Windows Task Scheduler under `NT AUTHORITY\SYSTEM` (`S-1-5-18`).
   - Triggered on Windows Update `Event ID 19` in `Microsoft-Windows-WindowsUpdateClient/Operational` to verify and maintain system compliance.

6. **Local REST API Worker (Kestrel):**
   - Headless background worker listening exclusively on `127.0.0.1:5123`.
   - Endpoints:
     - `GET /api/status`: System & rule compliance state.
     - `GET /api/rules`: Active rule definitions.
     - `POST /api/rules/apply`: Dry-run simulation or transactional execution.
     - `GET /api/audit/history`: Audit trail and transaction journal.
     - `POST /api/audit/revert/{id}`: Full transaction rollback.
     - `POST /api/audit/revert/{id}/category/{category}`: Selective category rollback.
     - `GET /api/watchdog`: Watchdog installation status.

---

## 🚀 Building & Running

### Prerequisites
- Windows 10 22H2 (Build 19045+) or Windows 11 (Build 22000+)
- .NET 8.0 SDK (automatically configured in `%USERPROFILE%\.dotnet`)

### Build
Run `build.cmd` or:
```cmd
dotnet build DWS.Enterprise.sln -c Release
```

### Native AOT Publishing
Run `scripts\publish-aot.cmd` or:
```cmd
dotnet publish src\DWS.GUI\DWS.GUI.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true -p:PublishSingleFile=true -o publish\gui
dotnet publish src\DWS.Worker\DWS.Worker.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true -p:PublishSingleFile=true -o publish\worker
```
Binaries will be output to `publish\gui\DWS.GUI.exe` and `publish\worker\DWS.Worker.exe`.
