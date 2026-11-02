# TBOK-Inspired Modernization Implementation Plan for DWS 11

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate proven optimizations, real-time streaming console, robust System Restore handling, multi-user/default-profile deployment, and modern Windows 11 tweaks inspired by TBOK-Win11Optimizer into DWS 11.

**Architecture:**
- Enhance `MainDwsForm.cs` and `WindowsUtil.cs` with real-time process stdout/stderr streaming and non-blocking, append-only logging.
- Implement registry query-before-write logging (`MATCH`, `CREATE`, `CHANGE`, `ERROR`).
- Implement robust System Restore management with auto-enablement, VSS service checks, and 24h throttling bypass.
- Implement `Default\NTUSER.DAT` mount/update/unload for sysprep / new user accounts.
- Add modern Windows 11 tweaks (WPBT execution block, Search WebView override, OOBE/BypassNRO, HAGS, Gaming/MMCS responsiveness, corrected Widget policies).

**Tech Stack:** C# .NET Framework 4.0, WinForms, Windows Registry, WMI, PowerShell.

---

### Task 1: Real-time Output & Streamed Process Engine
- [ ] Refactor `_OutPut` and `_OutPutInvoke` in `MainDwsForm.cs`: eliminate quadratic `ReadAllText + WriteAllText`; use `File.AppendAllText`; use `TextBox.AppendText()`.
- [ ] Refactor `ProcStartargs` to stream stdout and stderr line-by-line into `_OutPut` asynchronously using `OutputDataReceived`. Remove legacy Russian OEM 866 encoding, use UTF-8/Console encoding.
- [ ] Add registry comparison helper that inspects values prior to writing and logs `MATCH`, `CREATE`, or `CHANGE`.

### Task 2: Robust Windows System Restore Engine
- [ ] In `WindowsUtil.cs` and `MainDwsForm.cs`, create `EnsureSystemRestoreReady()`:
  - Check if System Protection on `C:\` is enabled; if not, invoke `Enable-ComputerRestore`.
  - Configure `VSS` and `swprv` service startup to demand, start `VSS` if stopped.
  - Temporarily set `SystemRestorePointCreationFrequency` = 0 to bypass Windows 24-hour throttle.
- [ ] Call restore point creation with proper error reporting and cleanup of the frequency key.
- [ ] Ensure automatic fallback to registry backup if restore point fails.

### Task 3: Multi-User & Default Profile Hive Deployment (Sysprep / New Accounts)
- [ ] Implement `ApplyDefaultUserHiveTweaks()` in `MainDwsForm.cs`:
  - Mount `C:\Users\Default\NTUSER.DAT` via `reg.exe load HKU\DWS_DefaultProfile`.
  - Apply user-level privacy, Explorer, ads, and taskbar policies.
  - Unload via `reg.exe unload HKU\DWS_DefaultProfile`.
- [ ] Apply the same per-user policies to any active loaded user SIDs under `HKU\S-1-5-21-*`.

### Task 4: Modern Windows 11 Tweaks & Policy Fixes
- [ ] Add WPBT execution blocker: `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\DisableWpbtExecution = 1`.
- [ ] Add Search WebView override: `HKLM\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\1694661260\EnabledState = 1`.
- [ ] Add OOBE policies: `DisablePrivacyExperience = 1`, `BypassNRO = 1`.
- [ ] Fix Widget disable keys: `DisableWidgetsBoard = 1`, `DisableWidgetsOnLockScreen = 1` (currently 0 in DWS).
- [ ] Add Gaming & Responsiveness tweaks: HAGS (`HWSchMode = 2`), `OptimizationsForWindowedGames = 1`, `NetworkThrottlingIndex = 0xffffffff`, `SystemResponsiveness = 10`.
- [ ] Add BitLocker safety check before altering boot policies.

### Task 5: Compilation, Verification & Final Report
- [ ] Compile solution using `build.cmd` and verify 0 errors, 0 warnings.
- [ ] Test/verify registry backup and restore mappings.
- [ ] Produce comprehensive final report detailing all improvements, comparisons, and technical specifics.
