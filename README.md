<p align="center">
  <img src="Assets/VoltrexQuestLogo.png" alt="Voltrex Quest Logo" width="128" height="128" />
</p>

<h1 align="center">Voltrex Quest</h1>

<p align="center">
  A native Windows 11 WinUI 3 desktop application for browsing Discord Quest-supported games, managing a personal game library, and launching and monitoring game sessions.
</p>

---

## Features

- **Home Dashboard**: Real-time statistics calculated from local library and session history data, recently played sessions, recently added games, and quick actions.
- **Discover Games**: Fast local search and filtering across the bundled `Data/gamelist.json` catalog with cover artwork, executable details, platform/overlay support tags, and one-click addition to your personal library.
- **My Games**: Persistent personal library stored in `%LOCALAPPDATA%\VoltrexQuest\library.json` with responsive 4-column card layout, sorting, search, custom executable configuration, store launching, and dummy process launching.
- **Game Session Monitor**: Dedicated WinUI 3 companion window (`SessionWindow`) displaying game artwork, live process state (`Initializing`, `Launching`, `Running`, `Stopped`, `Failed`), PID, process name, start time, elapsed duration, and session termination with confirmation.
- **Dummy Game Runner**: Lightweight native Win32 C++ runner (`DummyGameRunner.exe`) copied dynamically to match the target executable name (for example `wwm.exe`) so Quest/process detection tests can run without installing full titles.
- **Session History & Storage Management**: Local persistence for library entries, app settings, and session history under `%LOCALAPPDATA%\VoltrexQuest\`.

## Technology Stack

- **C# / .NET 8** (`net8.0-windows10.0.19041.0`)
- **WinUI 3 & Windows App SDK**
- **MVVM** with `CommunityToolkit.Mvvm`
- **System.Text.Json** for catalog, library, settings, and session history serialization
- **Native Win32 C++** (`DummyGameRunner`) for standalone zero-dependency dummy game windows

## Building & Running

### Prerequisites

- Windows 10 (1809+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022/2026 with the **Windows application development** workload (or Windows App SDK build tools)

### Build from CLI

```powershell
dotnet build voltrex-quest.slnx -p:Platform=x64
```

### Build Windows Installer (Inno Setup 6)

```powershell
.\Installer\build-installer.ps1 -Version 1.0.0
```

This publishes a self-contained unpackaged `win-x64` build and compiles [Installer/VoltrexQuest.iss](Installer/VoltrexQuest.iss) into `Installer/Output/Voltrex-Quest-Setup-1.0.0-win-x64.exe`.

### Project Layout

```text
voltrex-quest/
├── Assets/                 # App icons, MSIX tile/splash assets, and VoltrexQuestLogo.png
├── Controls/               # Reusable UI controls (e.g., GameCardControl)
├── Converters/             # XAML value converters
├── Data/                   # Bundled gamelist.json catalog
├── DummyGameRunner/        # Native Win32 C++ dummy game window source & binary
├── Helpers/                # Responsive grid layout helpers
├── Installer/              # Inno Setup script (VoltrexQuest.iss) & build script
├── Models/                 # Game, Session, Library, and History models
├── Services/               # Catalog, Library, Launcher, Settings, and Session History services
├── ViewModels/             # MVVM ViewModels for Main, Home, Discover, Library, Settings, Session
└── Views/                  # WinUI 3 Pages and SessionWindow
```

---

**Developed by Mohomed Ziard | Voltrex Digital**