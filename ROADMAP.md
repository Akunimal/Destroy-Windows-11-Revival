# Roadmap & Status

This file tracks what is already done in the **Destroy Windows 11 Revival** port
and what is still left to do. It is meant to be kept up to date as work lands.

## Current status

The core port from the original `Destroy-Windows-10-Spying` to a Windows 11-aware
build is **done and committed** on the `win11` branch.

### Done

- **Windows 11 detection** (build `>= 22000`) with a tri-state split: Windows 7/8.1,
  Windows 10 and Windows 11 each get their own settings panel. Detection reads
  `CurrentBuildNumber` (the `ProductName` registry string still reports "Windows 10"
  on some Windows 11 builds, so it cannot be trusted).
- **App removal rewritten to use exact package names** (`DWS/lib/Win11Apps.cs`),
  removing for all users and deleting provisioned copies. No more wildcards that
  could also match Notepad/Sticky Notes.
- **New Windows 11 app groups** (Copilot/AI Hub, Teams, new Outlook, Clipchamp,
  Widgets, PC Manager/misc) with checkboxes, translations (en-US + es-ES) and
  `Professional Mode` gating.
- **Windows 11 registry tweaks** (`ApplyWin11Tweaks()`): Copilot, Recall/AI,
  Widgets, Start/Explorer ads, Bing/Cortana, AI-service manual start, auto
  device-encryption prevention. Every value was verified line-by-line against the
  Win11Debloat regfiles and the shipped ADMX templates on a real Windows 11 26H2
  (build 26300) system.
- **Telemetry services disabled (not deleted) by default**; deletion is an opt-in
  expert option.
- **Hosts-only blocking.** The Windows Firewall is never touched; the old `netsh`
  rules for `explorer.exe`/`WSearch` (which broke Store, Update and Explorer) were
  removed. The 2015 IP-blackhole list is expert-mode only.
- **Removed the insecure HTTP in-app auto-update.**
- **Branding** updated to "Destroy Windows 11 Spying" / DWS 11 (assembly info,
  manifest with `supportedOS` entries for Win7/8/8.1/10/11, form changelog).

### Known limitation (not a code bug)

- The port **could not be compile-verified in the authoring environment**: the only
  available MSBuild ships the legacy **C# 5** compiler, while this project (already
  upstream) uses **C# 6** string interpolation (`$"..."`). Building requires Visual
  Studio / MSBuild 14+ with the .NET Framework targeting pack. Structural checks
  (XML validity, control declarations, brace balance, generated PowerShell syntax)
  all pass, and every build error observed was `CS1056` (interpolation), never a
  structural error.

---

## To do

These are the next steps, roughly in priority order.

### 1. Remove leftover links / references

- [ ] The in-app **About / "Source code" link** still points at the original
      upstream repository. Point it at this repo
      (`https://github.com/Akunimal/Destroy-Windows-11-Revival`) or remove it.
- [ ] Search the codebase for any remaining `github.com/Nummer/...` or
      `github.com/spinda/...` URLs (in forms, resources or comments) and update or
      remove them.
- [ ] Decide whether to keep the historical upstream credit in the About text (as
      attribution) while dropping it as an active "download/source" link.

### 2. Windows 11 visual refresh

The UI still uses the original flat WinForms look from the Windows 10 era. A visual
pass is needed to make it feel at home on Windows 11:

- [ ] Modern color palette / dark-mode-friendly styling (mica-like background,
      softer neutrals, accent color on primary buttons).
- [ ] Rounded corners and updated spacing/typography (Segoe UI Variable).
- [ ] Restyle checkboxes, buttons and the progress bar to match the Windows 11
      design language.
- [ ] Revisit layout: the settings panel now scrolls (`AutoScroll`) because of the
      extra Windows 11 options — a cleaner two-column or grouped layout would suit
      more items better.
- [ ] Consider `DarkMode` support (the app already has a `StealthMode`/theme hook to
      build on).

### 3. Improved application icon

- [ ] Replace the current icon with a modern, higher-resolution design (the original
      is low-res and dated).
- [ ] Export a proper `.ico` with the full size set (16/24/32/48/64/128/256 px).
- [ ] Match the new Windows 11 visual style (see above) so the icon, window and UI
      feel like one product.

### 4. Build & release pipeline

- [ ] Compile with Visual Studio / MSBuild 14+ and fix any residual issues (the
      CS1056-only errors should disappear with a C# 6+ compiler).
- [ ] Add a CI workflow (GitHub Actions with `windows-latest` + MSBuild) so every
      push produces a verified `Release` build.
- [ ] Publish a first tagged release with a signed or at least checksummed binary.

### 5. Nice to have

- [ ] Extend translations for the new Windows 11 checkboxes beyond en-US/es-ES.
- [ ] Review and refresh the 2025 hosts blocklist over time.
- [ ] Add a restore/undo feature for the Windows 11 registry tweaks.

---

_Maintained as part of the DWS 11 Revival port. Update this file as items land._
