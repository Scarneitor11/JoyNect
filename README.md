# JoyNect

JoyNect connects Nintendo Switch controllers to Windows and presents them to games as standard Xbox 360 pads through XInput. It runs as a normal user, owns the controller connection and the virtual pad it forwards, and keeps working from the notification area once you close the window.

It is built for Windows 10 and Windows 11. The release package below is a self-contained Windows x64 app, so it does not require .NET to be installed on the target machine.

## Getting the app

The easiest way to try it is the self-contained release package:

- [JoyNect-1.0.0-win-x64.zip](JoyNect-1.0.0-win-x64.zip)

Extract it and run **JoyNect.exe**. The executable is in

```
artifacts/JoyNect-1.0.0-win-x64/JoyNect.exe
```

If you clone the repository and build it yourself, the same executable is produced by:

```bash
dotnet publish src/JoyNect.App/JoyNect.App.csproj -r win-x64 --self-contained -c Release -o artifacts/JoyNect-1.0.0-win-x64
```

The solution also contains a CLI host and a screenshot capture runner, but the main app is `JoyNect.exe`.

## Before you play

JoyNect itself does not install a driver. Games see the virtual Xbox 360 pad through the ViGEmBus driver.

- If ViGEmBus is already installed, JoyNect just works.
- If it is missing, JoyNect tells you on first run and links to the official driver package from the first-run guide and the Diagnostics page.

Without ViGEmBus, controllers still connect in JoyNect, but games will not see a pad.

## First run

When you first start the app, it shows a short guide:

1. What JoyNect does.
2. Whether the driver is installed, with a way to check again.
3. How to pair a controller.

You can skip the guide at any time. The same information is available later on the Home and Diagnostics pages.

After the guide, JoyNect starts in the foreground. From then on it lives in the notification area and keeps forwarding controllers until you exit it there.

## Using it

- Click the system tray icon to open the window.
- Choose a page from the rail on the left: Home, Devices, Mapping, Motion, Lights, Diagnostics, About, or Settings.
- Pair a Switch controller over Bluetooth or USB.
- Edit the mapping on the Mapping page if you want buttons and sticks in a different arrangement.
- Use the Motion page to see live controller motion.
- Use the Lights page for controller LED behavior where supported.

Only one JoyNect instance runs at a time. If you try to start it again, it brings the existing instance forward instead of opening a second copy.

## Motion and the DSU gyro server

JoyNect has a UDP server that emulators and other tools can use for motion. It speaks the Cemuhook DSU protocol exactly as emulators expect it, on the port configured in Settings. When the server is enabled, every connected controller that has motion gets live gyro and accelerometer data published over UDP.

The Motion page shows the controller's motion in real time.

## Command line

JoyNect.exe accepts a few commands:

- `JoyNect.exe` — start the app normally.
- `JoyNect.exe --tray` — start minimized to the notification area.
- `JoyNect.exe --capture [folder]` — render the app's screens to PNG without showing a window. The default folder is `captures`.
- `JoyNect.exe --capture --dark [folder]` — render the dark variants as well.
- `JoyNect.exe help` — print the built-in command line help.
- `JoyNect.exe diagnose` — write a diagnostic report.
- `JoyNect.exe driver` — check driver state from the command line.
- `JoyNect.exe enumerate` — list HID devices.
- `JoyNect.exe api-dump` — dump the public API surface.

There is no persistent console when you start the GUI normally. Command line output goes to the console when there is one.

## Building from source

The repository is a .NET solution with four projects:

- `JoyNect.Core` — headless logic: controller sessions, mapping, motion, diagnostics, and the DSU server.
- `JoyNect.Ui` — the Material 3 design system and shared controls.
- `JoyNect.App` — the desktop app.
- `JoyNect.Cli` — the command line host.

To build everything:

```bash
dotnet build JoyNect.sln -c Release
```

To run the tests:

```bash
dotnet test JoyNect.sln -c Release
```

The app project is Windows-only by design, because it depends on WPF, the HID stack, and ViGEmBus.

`JoyNect.App` builds from the repository because its icon resources are optional. `assets/app.ico` is
the app icon and the tray icon source; `assets/app.png` is an optional tray image that the build will
skip if it is absent. If you clone the repo and only have the icon file, the app still builds.

## Repository layout

- `src/JoyNect.App` — desktop application.
- `src/JoyNect.Core` — platform and controller logic.
- `src/JoyNect.Ui` — UI components and theme.
- `src/JoyNect.Cli` — CLI host.
- `tests/JoyNect.Core.Tests` — unit tests.
- `tools/` — repo tooling.
- `artifacts/JoyNect-1.0.0-win-x64` — self-contained Windows x64 release layout.
- `JoyNect-1.0.0-win-x64.zip` — the same layout, zipped for download.

The repository ignores build output, IDE files, and local tooling virtual environments. Release artifacts and repo-root branding images are also excluded.

## Files in the release package

The release package contains the app and the .NET runtime it needs.

- `JoyNect.exe` — the app.
- The DLLs and runtime files required to run it without a separate .NET installation.
- The embedded app icon and asset resources.

Logs are written to `%LOCALAPPDATA%\JoyNect\logs\joynect.log`.

## License

JoyNect is released under the MIT License. See [LICENSE](LICENSE).

## Status

JoyNect is developed as an open desktop app for Switch controllers on Windows.
