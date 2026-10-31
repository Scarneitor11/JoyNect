# JoyNect feature inventory

## App shell / wire harness
- Shortcuts: Navigate  Renders every view. Live. AppState-driven.
- Navigation: Open Play Page Opens HomePage. Live.
- MainWindow: Onboarding overlay on first run; system tray host; window state (minimize to tray shown). Live.
- Startup: Windows startup registration. Live, gated.
- Tray: tray icon with minimize-to-tray and restore. Live.

## Onboarding
- First-run overlay: account sign-in, consent to diagnostics sharing, controller-pairing guidance. Live (coded; needs runtime + auth integration to actually surface).
- Claimed but unproven at runtime: onboarding actually shows and completes on first launch. No runtime check yet.

## Authentication / account (SkillStack, Sign in with Freebuff)
- README explicitly says app never authenticates; no account does anything. Present in docs; not implemented.
- Claimed in model but absent from code: any "account" feature. No evidence.

## Navigation rail / view hosting
- NavRail: fixed icon rail, selected state, hover, keyboard focus. Live.
- All pages are ctor-injected and rendered through AppPage. Live.

## Home / Play page
- Start/stop capture. Live.
- Capture progress state (stripe: connecting / connecting to controller / receiving / idle). Live.
- Reconnecting banner when controller drops mid-session. Live.
- Binds to AppState.Mode, AppState.Controller, AppState.Session, AppState.MotionEnabled. Live.
- Live display of controller name/model when connected. Live.

## Controller connection (devices)
- Feature: auto-discover Switch controllers on Bluetooth/USB.
  - Evidence: `IDeviceDiscovery` + `HidControllerTransport` + `ControllerManager` + `SwitchControllerSession` + `DeviceEnumerator`. Live code; gated by Win32 HID APIs and driver/runtime.
- Feature: connect to a selected controller.
  - Evidence: `ControllerManager.ConnectController`, session lifecycle. Live code.
- Feature: reconnect after disconnect.
  - Evidence: `ControllerLinkState`, reconnect path in session page state, banner in HomePage. Live code.
- Feature: show connected controller and its model/identifier.
  - Evidence: `DeviceInfo`, `DeviceItem`, `SessionStatistics`, HomePage. Live.
- Feature: show diagnostics about the USB/Bluetooth link (this is the Diagnostics page, see below).
  - Evidence: `DiagnosticReport`, `IntervalHistogram`, DiagnosticsPage. Live code.

## Controller identity / firmware
- Feature: read device info (firmware version, MAC, controller type) via subcommand 0x02.
  - Evidence: `DeviceInfo.TryParse`, `DeviceInfo.MacAddressText`, `DeviceInfo.FirmwareText`. Live.
- Claimed but unverified: device info is actually displayed to the user. Shell/model has the data; no page currently surfaces FirmwareText/MacAddressText. Gap: display surface missing.

## Button mapping / pad mapping
- Feature: map Switch buttons to an Xbox 360 virtual pad.
  - Evidence: `SwitchButtons`, `Xbox360Target`, `PadComposer`, `MappingProfile`, MappingPage. Live code; mapping state in AppState. Tests: `PadComposerTests`.
- Feature: invert axes.
  - Evidence: `StickSettings`, `PadComposerTests` (axis inversion cases). Live; tests cover it.
- Feature: adjustable stick sensitivity / response curves.
  - Evidence: `StickProcessor`, `StickSettings`, CurvePreview, SettingsPage exposure. Live code; tests: `StickProcessorTests`. UI: CurvePreview renders a curve.
- Feature: DualSense-style toggle for stick behaviour (Joy-Con mode vs single-stick assumptions).
  - Evidence: `JoyConMode` enum in mapping settings. Live carrier; UI binding should exist on mapping page.
- Feature: per-button enable/disable or remapping.
  - Evidence: mapping model. Live code. UI surface claimed on MappingPage.
- Claimed but weak: saved/loadable mapping profiles. `MappingProfile` exists but persistence story is thin; snapshot/readback not clearly wired. Gap: no strong "profiles" feature evidence.

## Motion passthrough / motion output
- Feature: enable IMU sampling (subcommand 0x40), set IMU sensitivity (0x41).
  - Evidence: `ProtocolConstants` + `SwitchControllerSession` motion path + `MotionPipeline`. Live code.
- Feature: motion passthrough to supported receivers.
  - Evidence: `DsuMotionServer`, `MotionPipeline`, `MotionScope` control, MotionPage. Live code; tests: `DsuProtocolTests`, `MotionPipelineTests`, `DsuMotionServerTests`.
- Feature: visualize motion (scope/graphs).
  - Evidence: `MotionScope` control. Live UI control.
- Feature: toggle motion on/off from UI.
  - Evidence: MotionPage + Settings exposure. Live code path.

## Rumble
- Feature: send rumble to the controller (left/right actuators).
  - Evidence: `RumbleEncoder`, `OutputFrames`, session output path. Live code; tests: `RumbleEncoderTests`.
- Feature: rumble intensity control / rumble UI.
  - Evidence: SettingsPage rumble exposure, HomePage rumble affordance in model. Live code path.
- Claimed but weak: rumble "patterns" or custom waveforms beyond intensity. No separate feature evidence beyond encoder. Gap: no advanced rumble profile feature.

## Lights (player LEDs)
- Feature: Set player lights on the controller (subcommand 0x30).
  - Evidence: `ProtocolConstants.SubcommandSetPlayerLights`, `OutputFrames.BuildPlayerLightArguments`, `PlayerLightForSlot`. Live.
- Feature: Set home light (subcommand 0x38).
  - Evidence: `ProtocolConstants.SubcommandSetHomeLight`. Live carrier; UI path less clear.
- Feature: light UI (LightsPage).
  - Evidence: LightsPage. Live page; behavior depends on session/controller being live.
- Claimed but unproven at runtime: lights actually change on a real controller from the page. Code exists; runtime verification missing.

## Virtual Xbox 360 pad output
- Feature: output an Xbox 360 gamepad over ViGEm.
  - Evidence: `IVirtualPadBackend`, `ViGEmBackend` placeholder/shell, `Xbox360PadState`, pad target model. Live code surface, but `ViGEmBackend.cs` is tiny/placeholder. Gap: real ViGEm driver integration appears not implemented; virtual pad output is the biggest "supposedly works but no evidence" item.
- Feature: 4-slot player mapping to virtual pad.
  - Evidence: `OutputFrames.PlayerLightForSlot` + pad model. Live carrier; virtual output backend is the blocker.

## Diagnostics page
- Feature: show link/render diagnostics: interval histograms, percentiles, worst-case stalls.
  - Evidence: `IntervalHistogram`, `DiagnosticReport`, DiagnosticsPage. Live code.
- Feature: show diagnostic summary text.
  - Evidence: `IntervalHistogram.Describe`. Live.
- Claimed but weak: diagnostic report export/share. DiagnosticReport exists; export surface not clearly wired. Gap: no strong export feature.

## Settings page
- Feature: app settings persistence.
  - Evidence: `AppSettings`, `SettingsStore`, SettingsPage. Live code; tests: `SettingsStoreTests`.
- Feature: settings UI for:
  - startup / minimize-to-tray behavior
  - diagnostics sharing consent
  - mapping sensitivity/curve
  - rumble
  - motion
  - player lights
  - About/version info
  - Live page; each subfield mapped to AppState/SettingsStore where applicable.

## Diagnostics consent / telemetry
- Feature: opt in/out of diagnostics sharing.
  - Evidence: AppSettings + SettingsPage + README explicit "app never authenticates, only shares diagnostics if you opt in". Live carrier.
- Claimed but unproven: an actual sharing/upload path. README says sharing is optional; code has consent flag but no real upload destination claimed. Gap: no evidence of a real telemetry pipeline; likely not implemented.

## Session statistics / runtime telemetry
- Feature: track session stats: connection state, report intervals, session time.
  - Evidence: `SessionStatistics`, `ControllerLinkState`, AppState session modeling. Live code.
- Feature: display session stats.
  - Evidence: HomePage session state + DiagnosticsPage. Live carrier.
- Claimed but weak: rich per-session export. Present in model; display/export is thin.

## About page
- Feature: app name, version, license link, attribution.
  - Evidence: AboutPage. Live code.
- Claimed but weak: version number actually populated from assembly. AboutPage exists; runtime version population should be verified.

## Capture / demo mode (DemoState)
- Feature: capture a replay/demo of controller input.
  - Evidence: `CaptureRunner`, `DemoState`, `CliHost` capture path. Live code surface.
- Feature: CLI capture.
  - Evidence: `CliOptions`, `CliHost`, `CaptureProgram`, `Program.cs`. Live CLI entry points.
- Claimed but weak: demo playback. Capture exists; playback path is thin/unclear. Gap: no strong playback feature evidence.

## Headless CLI
- Feature: run JoyNect as a headless CLI.
  - Evidence: `JoyNect.Cli` project, `Program.cs`, `CliHost`. Live project.
- Feature: CLI commands for capture/runtime control.
  - Evidence: `CliOptions` + `CliHost`. Live code.
- Claimed but weak: full parity with desktop app commands. CLI exists; coverage vs app features is the question.

## Material 3 design system (JoyNect.Ui)
- Feature: Material 3 controls: buttons, icon buttons, cards, chips, sliders, switches, segmented buttons, select, progress, color swatch.
  - Evidence: `JoyNect.Ui` controls + tests: `M3ControlGeometryTests`, `M3ControlMotionTests`. Live code + tests.
- Feature: consistent theme (dark/light scheme).
  - Evidence: `M3Theme`, `M3Scheme`, `M3ThemeManager`, `M3Motion`, theme in app startup. Live code.
- Feature: animations/motion (entrance, micro-interactions).
  - Evidence: `M3Motion`, `M3ControlMotionTests`. Live code + tests.
- Claimed but weak: full design-system completeness vs every Material component. Only a subset is implemented; gap is "additional components" not "missing core controls".

## Build / packaging
- Feature: Windows desktop app (WPF).
  - Evidence: `JoyNect.App` WPF project, `app.manifest`, MainWindow. Live build artifact target.
- Feature: app manifest (elevated/privilege requests, DPI awareness, etc.).
  - Evidence: `app.manifest`. Live.
- Feature: reproducible repo packaging (tooling).
  - Evidence: `make-repo-artifacts.ps1`, `prepare-repo.ps1`, `tools/create-github-repo.mjs`, `tools/generate-icon.py`, `tools/archil_store.py`. Live tooling.

## Testing / verification
- Feature: unit tests.
  - Evidence: `JoyNect.Core.Tests` suite covering protocol, mapping, motion, settings, design, UI motion/geometry, device session. Live tests.
- Claimed but weak: end-to-end / integration / runtime tests. No evidence of real hardware or full app runtime tests. Gap: no live E2E verification.

## Explicit README claims vs evidence
- README: Switch controller adapter + Xbox 360 virtual gamepad. Evidence: connection code strong; virtual pad output backend is placeholder. Gap: virtual pad output.
- README: "no authentication, only optional diagnostics sharing." Evidence: consent flag exists; real sharing pipeline has no evidence. Gap: telemetry upload.
- README: Windows startup, minimize to tray. Evidence: StartupRegistration + TrayIconHost + SettingsPage. Live.
- README: material design pages. Evidence: M3 controls + tests. Live.

# Biggest "supposedly works but no real evidence" list
1. Virtual Xbox 360 pad output over ViGEm — backend is a placeholder; this is the core "output" feature.
2. Actual wireless/USB controller connect + rumble + motion + lights working on real hardware — code is strong, but there is no runtime/hardware verification evidence in this repo.
3. Diagnostics sharing — opt-in flag exists, upload endpoint/behavior has no evidence.
4. Demo capture playback — capture exists, playback story is thin.
5. Saved mapping profiles / load/save workflows — model exists, full feature story is thin.
6. Onboarding runtime completion — coded overlay exists; needs runtime + auth + pairing flow to actually do something.
7. About-page version population at runtime — should be verified.

# Notes on scope
This repo is mostly a real signal-chain codebase (protocol parsing, calibration, mapping, motion, rumble, diagnostics) plus a WPF shell with Material 3 controls and a CLI. The single biggest missing "feature" is virtual gamepad output: everything upstream (connect, read, map, rumble, light, motion) can exist without producing an actual Xbox 360 gamepad, and `ViGEmBackend.cs` is not a real implementation.
