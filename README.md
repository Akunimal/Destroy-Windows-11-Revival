# NOTE

This is a fork of
[Nummer/Destroy-WIndows-10-Spying](https://github.com/Nummer/Destroy-Windows-10-Spying)

I don't trust the official binary releases, so I've done the following:

- Forked the project
- Removed some binary files included in the repository
- Resaved all image files
- Manually reviewed all other files in the repository
- Removed/swapped references to upstream from the application

The original README file is reproduced below.

# Destroy Windows 11 Spying (DWS 11)

A Windows 11 focused fork of
[spinda/Destroy-Windows-10-Spying](https://github.com/spinda/Destroy-Windows-10-Spying)
(a clean fork of [Nummer/Destroy-Windows-10-Spying](https://github.com/Nummer/Destroy-Windows-10-Spying)).

### What's new in DWS 11

- Detects Windows 11 (build >= 22000), Windows 10 and Windows 7/8.1 separately.
- New Windows 11 app removal with **exact package IDs** (no wildcards that could
  delete Notepad/Sticky Notes): Copilot + AI Hub, Teams, new Outlook, Clipchamp,
  Widgets, PC Manager, Get Started, Feedback Hub, Bing Search, Quick Assist, etc.
  Removal is done for **all users** and also removes provisioned packages so they
  don't return on new accounts.
- New "Windows 11 tweaks" registry block: Copilot off, Recall / AI data analysis
  off, Widgets off, Start/Explorer ads & suggestions off, Bing/Cortana in search
  off, AI service manual start, and auto device-encryption prevention.
- Modernized telemetry handling: DiagTrack et al. are **disabled** by default
  (not deleted — Windows 11 re-registers them and deletion can break things).
  Deleting the services is an opt-in EXPERT option.
- Updated hosts blocklist to 2025 telemetry/ad endpoints. DWS 11 writes **only**
  to the HOSTS file and never touches the Windows Firewall (the old netsh rules
  for explorer.exe/WSearch broke Store, updates and Explorer on Win10/11).
- The old 2015 IP blackhole route list is kept but is EXPERT-mode only.
- Removed the insecure in-app auto-update (it fetched an AssemblyInfo.cs over
  plain HTTP).
- Targets .NET Framework 4.0; on Windows 11 it runs on the in-place upgraded
  4.8/4.8.1 runtime that ships with the OS.

### Original DWS features (still present)

- Remove spyware modules, spying services and Metro/UWP apps
- Add spying domains to the HOSTS file
- Remove Office 2016 telemetry
- 19 interface languages
- Open source

### Building

Open `DWS_Lite.sln` in Visual Studio and build, or run MSBuild:

```
msbuild DWS_Lite.sln /p:Configuration=Release
```

Requires .NET Framework 4.8 targeting pack.

### License

Licensed under the Apache License, Version 2.0. See `LICENSE` (upstream).

Copyright 2015 Nummer. DWS 11 modifications are provided under the same license.

---

The original upstream README is preserved below.

# Destroy Windows 10 Spying

### Features
  
- Remove all spyware modules
- Remove spying apps
- Add spying domains to hosts file!
- Remove spying services
- Remove Windows 10 Metro Apps
- Support Windows 7/8/8.1/10 and Server 2008-2012 R2
- Remove Office 2016 telemetry
- Open Source!

### Determination of the system language

We get the language from the command-line arguments to the function:
```c#
private string _GetLang(IEnumerable<string> args)
```
Next we set language in function:
```c#
private void SetLanguage(string currentlang = null)
```
And call function to change the language:
```c#
private void ChangeLanguage()
```

### Copyright 2015 Nummer

[Twitter @nummerok](https://twitter.com/nummerok)

### License

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

  * [http://www.apache.org/licenses/LICENSE-2.0](http://www.apache.org/licenses/LICENSE-2.0)

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
