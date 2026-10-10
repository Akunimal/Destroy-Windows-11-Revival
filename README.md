# Destroy Windows 11 Revival (DWS 11)

A maintained revival of **Destroy Windows 10 Spying**, updated for Windows 11.

This project is a direct continuation of the original DWS tool by *Nummer*, carried
through *spinda*'s clean fork, and now updated to properly support Windows 11 —
its newer inbox apps, privacy-related registry policies and telemetry handling.

> **Status:** active revival. See [ROADMAP.md](ROADMAP.md) for the current status
> and the list of what is still left to do.

## Overview

Destroy Windows 11 Revival is a small, open-source utility that removes the
telemetry, spying services and preinstalled bloatware from Windows. It is a single
WinForms binary and runs on **Windows 7, 8.1, 10 and 11**.

This is *not* a rewrite — it keeps the original DWS codebase and behaviour, and
layers the Windows 11 support on top of it.

## What's new in DWS 11

- **Windows 11 detection** (build `>= 22000`) alongside Windows 10 and Windows 7/8.1,
  each with its own settings panel.
- **Windows 11 app removal by exact package name.** New groups for Copilot + AI Hub,
  Microsoft Teams, the new Outlook, Clipchamp, Widgets, PC Manager, Get Started,
  Feedback Hub, Bing Search, Quick Assist and more. Packages are removed for **all
  users** and the provisioned (new-account) copies are deleted too, so they don't
  come back. Exact names are used instead of wildcards, so nothing like Notepad or
  Sticky Notes gets caught by accident.
- **Windows 11 registry tweaks** (verified against the Win11Debloat regfiles and the
  shipped ADMX templates): Copilot off, Recall / AI data analysis off, Widgets off,
  Start/Explorer ads and suggestions off, Bing/Cortana-in-search off, AI fabric
  service set to manual start, and auto device-encryption prevention.
- **Modernized telemetry handling.** Services like DiagTrack are **disabled** by
  default rather than deleted (Windows 11 re-registers them, and deleting them can
  break things). Deleting them is an opt-in *expert* option.
- **Updated hosts blocklist** with 2025-era telemetry/ad endpoints. The tool writes
  **only** to the HOSTS file and **never touches the Windows Firewall** (the old
  `netsh` rules for `explorer.exe`/`WSearch` broke the Store, Windows Update and
  Explorer on Windows 10/11).
- **Safer by default.** The 2015 IP-blackhole list is expert-mode only, and the
  insecure in-app auto-update (which fetched source over plain HTTP) was removed.

## Features (carried over from the original)

- Remove spyware modules, spying services and Metro/UWP apps
- Add spying domains to the HOSTS file
- Remove Office 2016 telemetry
- 19 interface languages
- Open source

## Requirements

- Windows 7 (RTM) or later, up to and including Windows 11
- .NET Framework 4.0 or later (Windows 11 ships with 4.8/4.8.1 in place)

## Building

Open `DWS_Lite.sln` in Visual Studio and build the `Release` configuration, or run
MSBuild from a Visual Studio Developer Command Prompt:

```
msbuild DWS_Lite.sln /p:Configuration=Release
```

The project is a classic (non-SDK) .NET Framework project, so it needs the .NET
Framework build tools / targeting pack installed with Visual Studio.

## Credits & License

- Original tool by **Nummer** — see [Nummer/Destroy-Windows-10-Spying](https://github.com/Nummer/Destroy-Windows-10-Spying)
- Clean upstream fork by **spinda** — see [spinda/Destroy-Windows-10-Spying](https://github.com/spinda/Destroy-Windows-10-Spying)

Licensed under the Apache License, Version 2.0. See [`LICENSE`](LICENSE).

Copyright 2015 Nummer. The DWS 11 modifications in this repository are provided
under the same license.
