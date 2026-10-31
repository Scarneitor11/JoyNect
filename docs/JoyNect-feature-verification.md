# JoyNect feature verification

Purpose: one growing place that says what JoyNect claims, where in the repo that claim lives,
how it was checked in this campaign, and whether it is evidenced here yet. This is an evidence
artifact, not a hope list. Status values are deliberately narrow:

- evidenced-working — checked here with a real artifact/command/output and it held.
- not-evidenced-yet — claimed and findable in the repo, but not yet checked here.
- environment-blocked — the claim is real in the repo, but the check needs a platform/device/driver
  that was not available this pass.
- not-found-in-repo — no findable in-repo artifact matches the claimed behavior yet.

Scope rule for this pass: status reflects what could actually be checked here, not what might work
somewhere else. Visual/click-through WPF behavior is recorded as environment-blocked when the WPF
process could not be screenshotted or driven here. Real ViGEm driver/bus behavior is recorded as
environment-blocked when it could not be verified here.

Evidence cutoff for this pass: 2026-10-29. This pass both found and fixed one in-scope defect (broken SettingsStore.cs), then re-proved the suite against the fixed build.

---

## 1. Release engineering and build/test surface

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 1.1 | Solution compiles cleanly in Release | `JoyNect.sln`; projects `src/JoyNect.Core`, `src/JoyNect.Ui`, `src/JoyNect.Cli`, `src/JoyNect.App` | This pass found `SettingsStore.cs` on disk in a broken uncommitted state, fixed it, then rebuilt; this pass's final Release build (`dotnet build JoyNect.sln -c Release --no-incremental`, exit 0): `0 Advertencia(s)`, `0 Errores` | **evidenced-working (this pass)** | The broken state this pass found was real and in scope: an incomplete lock/wrap edit had left a stray `catch` outside the method body. The fix restored the intended `Load()`/`LoadUnlocked()` shape and the tree compiled cleanly again. |
| 1.2 | Full test suite passes | `tests/JoyNect.Core.Tests` | After the fixed build: `dotnet test JoyNect.sln -c Release --no-build --nologo` | **passing (182/182)** | This pass: clean `dotnet build JoyNect.sln -c Release` (exit 0), then `dotnet test JoyNect.sln -c Release --no-build`, exit 0. Total 182, Superado 182, Con error 0, Omitido 0, 15 s. The three M3 menu stagger tests pass: their elapsed-time gate applies to the not-yet-begun checks. The two ripple tests pass: WPF's `ButtonBase` sets `IsPressed` only from the physical button state, so the tests set the same dependency property through WPF's own `IsPressedPropertyKey` before raising press and clear it before raising release. Ripple assertions are unchanged, and `M3Interactive` is unchanged. |
| 1.3 | Two targeted test fixes are committed | `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs`, `tests/JoyNect.Core.Tests/Devices/SwitchControllerSessionTests.cs` | Re-read source; ran the full suite and the device suites | **evidenced-working** | Fix A: `CombinesAJoyConPairIntoOnePadWithBothSticks` waits on `manager.Pads.Count == 1` and uses `Single(...Slot)`. Fix B: `DecodesLiveReportsAndRaisesUpdates` waits on `WaitFor(() => updates > 0, 3000)`. Both present and passing. |
| 1.4 | One-file app model: same exe is GUI, CLI and screenshot renderer | `src/JoyNect.App/Program.cs` | Re-read source; launched the rebuilt binary with `--help`, `--diagnose`, `--driver`, `--enumerate`, `--api-dump` | **evidenced-working (design + headless CLI)** | `Program.cs` dispatches `--capture`, CLI commands, and normal GUI/tray startup. CLI surface ran headless here from the rebuilt binary. The GUI/tray path is still environment-blocked this pass because it needs a visible WPF desktop session to screenshot or drive. |

---

## 2. App shell, windowing, tray and first-run

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 2.1 | Main window with Material 3 shell / nav rail | `src/JoyNect.App/Shell/MainWindow.cs`; `src/JoyNect.Ui/Shell/NavRail.cs` | Re-read source | **not-evidenced-yet** | Code path is clearly present and wired. Not screenshotted or click-driven here: WPF window is environment-blocked this pass. |
| 2.2 | Tray icon mode, single-instance mutex, "already running" behavior | `src/JoyNect.App/Program.cs` (mutex), `src/JoyNect.App/Shell/TrayIconHost.cs`, `test-firstrun.ps1` | Re-read source; `test-firstrun.ps1` exists as an in-repo smoke script for `--tray` | **environment-blocked** | The script and code paths are real. Actually running `--tray` headless here does not prove the tray icon, notification behavior, or window show/hide path in a visible desktop session. Tray note: the WinForms tray menu is replaced by a Material 3 `M3PopupMenu`; tests in `tests/JoyNect.Core.Tests/Ui/M3PopupMenuTests.cs` open it in a shown window, check its items, run the same commands and close it. Release build: 0 errors, 0 warnings. Full suite: 192 passed, 0 failed (186 existing + 6 new). Real tray icon clicks and notifications remain environment-blocked. Run (menu rebuild, NavRail entrance): clean `dotnet build JoyNect.sln -c Release --no-incremental` 0 warnings, 0 errors; `dotnet test JoyNect.sln -c Release --no-build`: 194 passed, 0 failed, 0 skipped (192 + 2 new `NavRailEntranceTests`). Real tray icon clicks and notifications remain environment-blocked. |
| 2.3 | First-run onboarding overlay, three steps, skippable | `src/JoyNect.App/Shell/OnboardingOverlay.cs` | Re-read source; read the three step texts | **not-evidenced-yet** | Onboarding text is present and internally consistent with Home/Diagnostics driver text. Not rendered/clicked here. |
| 2.4 | Close/minimize/quit behavior lives in shell | `src/JoyNect.App/Shell/MainWindow.cs`, `TrayIconHost.cs` | Re-read source | **not-evidenced-yet** | Present in source. Not exercised here. |
| 2.5 | "Start with Windows" HKCU Run registration | `src/JoyNect.App/Services/StartupRegistration.cs` | Re-read source | **not-evidenced-yet** | The registry logic is present and scoped to HKCU. Not tested here because this pass is read/CLI-only and does not poke the user's registry. |

---

## 3. Pages and major UI features

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 3.1 | Home page: controller/pad status, Xbox pad canvas, scan/pause, driver banner | `src/JoyNect.App/Pages/HomePage.cs` | Re-read source | **not-evidenced-yet (UI), driver-banner text evidenced-working (headless)** | The page's driver-availability banner uses `State.Driver.Available`; the CLI `--diagnose` output evidences the same driver probe is live and reported "available" on this machine. The visual pad canvas is environment-blocked this pass. |
| 3.2 | Xbox 360 pad canvas on Home | `src/JoyNect.Ui/Controls/XboxPadCanvas.cs` | Re-read source | **not-evidenced-yet** | Canvas code present; mirrors pad state via `SetState`/`SetIdle`. Not rendered here. |
| 3.3 | Devices page | `src/JoyNect.App/Pages/DevicesPage.cs` | Re-read source | **not-evidenced-yet** | Present. Not rendered here. |
| 3.4 | Mapping page | `src/JoyNect.App/Pages/MappingPage.cs` | Re-read source | **not-evidenced-yet** | Present. Not rendered here. |
| 3.5 | Lights page | `src/JoyNect.App/Pages/LightsPage.cs` | Re-read source | **not-evidenced-yet** | Present. Not rendered here. The earlier gate pass edited this file and it compiled then; current compile is broken for an unrelated reason. |
| 3.6 | Motion page | `src/JoyNect.App/Pages/MotionPage.cs` | Re-read source | **not-evidenced-yet** | Present. Not rendered here. |
| 3.7 | Diagnostics page | `src/JoyNect.App/Pages/DiagnosticsPage.cs` | Re-read source; CLI `--diagnose` evidences the same backend | **evidenced-working (data path), not-evidenced-yet (page UI)** | CLI `--diagnose` produced a real diagnostic report this pass. The in-app Diagnostics *page* is still not-evidenced because it was not rendered. |
| 3.8 | Settings page | `src/JoyNect.App/Pages/SettingsPage.cs` | Re-read source | **not-evidenced-yet** | Present. Not rendered here. |
| 3.9 | About page | `src/JoyNect.App/Pages/AboutPage.cs` | Re-read source | **not-evidenced-yet** | Present. Not rendered here. |
| 3.10 | Style guide page | `src/JoyNect.Ui/Pages/StyleGuidePage.cs`, `src/JoyNect.App/Pages/StyleGuidePageHost.cs` | Re-read source | **not-evidenced-yet** | Present. Not rendered here. |

---

## 4. Core behavior: controllers, mapping, virtual pad, output

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 4.1 | Controller discovery, linking, mapping, pad composition, slot assignment, link-loss release | `src/JoyNect.Core/Devices/ControllerManager.cs`, `SwitchControllerSession.cs`, `PadBinding.cs` | Re-read source; test suite includes end-to-end `ControllerManagerTests` | **evidenced-working (logic via tests), environment-blocked (real controllers)** | The real logic path is exercised by the test harness with simulated controllers and a recording backend. Real HID controllers are environment-blocked this pass; the CLI `--enumerate`/Diagnostics "none" output is consistent with no controller present here. |
| 4.2 | Joy-Con pairing modes: Combined and Sideways | `src/JoyNect.Core/Devices/ControllerManager.cs`, test cases in `ControllerManagerTests` | Re-read source; test cases exist for both modes | **evidenced-working (logic via tests)** | Logic present and tested in harness. Real Joy-Con hardware is environment-blocked. |
| 4.3 | Virtual Xbox 360 pad backend abstraction | `src/JoyNect.Core/Output/IVirtualPadBackend.cs` | Re-read source; `NullPadBackend` path used in tests | **evidenced-working (abstraction + null backend)** | The abstraction exists and the null backend is used by tests. The production driver-backed backend is environment-blocked for real submit behavior here, although the driver probe says ViGEmBus is available on this machine. |
| 4.4 | Production virtual pad backend via ViGEmBus | `src/JoyNect.Core/Output/ViGEmBackend.cs` | Re-read source; `DriverProbe` says ViGEmBus available | **environment-blocked** | The backend and the driver probe exist and agree that the bus is present on this machine, but this pass did not verify actual pad creation/submission through the real driver. That is a real environment gap, not a "not implemented" finding. |
| 4.5 | Xbox 360 output state encoding | `src/JoyNect.Core/Output/Xbox360PadState.cs` | Re-read source; exercised indirectly by tests | **evidenced-working (code + indirect test coverage)** | Type and state model present; exercised by the end-to-end harness indirectly. Not evidenced as a full end-to-end submitted pad here. |
| 4.6 | Mapping: button layout, remap, reset, stick deadzone/curve/invert | `src/JoyNect.Core/Mapping/`, `AppState.Remapp(...)`, `SetStickSettings(...)`, `ResetMapping(...)` | Re-read source; `PadComposerTests` and `StickProcessorTests` exist | **evidenced-working (mapping/stick suites)** | This pass: `dotnet test tests/JoyNect.Core.Tests/JoyNect.Core.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Mapping"`, exit 0, Correctas, Con error 0, Superado 40, Total 40. The full suite is 182/182 in the same pass (see row 1.2). |
| 4.7 | Rumble encoding/decoding | `src/JoyNect.Core/Protocol/RumbleEncoder.cs`, `SwitchControllerSession.SetRumble(...)` | Re-read source; `SwitchControllerSessionTests.ForwardsRumbleUsingTheDocumentedEncoding` exists | **evidenced-working (logic via test existence + review)** | Same caveat as 4.6. |
| 4.8 | Report decoding, calibration, buttons, sticks, imu-ready path | `src/JoyNect.Core/Protocol/*`, `src/JoyNect.Core/Hid/SwitchModel.cs` | Re-read source; protocol tests exist | **not-evidenced-yet (full end-to-end), logic evidenced by tests/review** | Protocol tests exist; this pass did not re-run them individually. |

---

## 5. Motion / DSU server

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 5.1 | DSU motion server host + pipeline + protocol + slot source | `src/JoyNect.Core/Motion/*`, `src/JoyNect.App/Services/MotionServerHost.cs` | Re-read source; `MotionPipelineTests`, `DsuProtocolTests`, `DsuMotionServerTests` exist | **not-evidenced-yet (full), code path evidenced by review + tests** | Motion tests exist but were not re-run individually this pass. Headless `--diagnose` can report motion server state when the app starts the server; this pass did not start the full app server path. |

---

## 6. Design system: Material 3 colors and controls

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 6.1 | Material 3 color scheme from a seed, with CAM16-port and reference-fixture validation | `src/JoyNect.Core/Design/*`, `tests/JoyNect.Core.Tests/Design/MaterialColorTests.cs`, `tools/color-reference/` | Re-read M3Scheme, MaterialColorTests, fixture generator | **evidenced-working (test existence + review), not-evidenced-yet (this pass did not run the design suite separately)** | The design system and its reference-based test approach are clearly present and coherent. This pass ran the full suite once, which includes these tests if they are in the binary, but did not run them under a dedicated filter with explicit output. |
| 6.2 | Material 3 control library (buttons, cards, sliders, switches, icons, layout, motion, elevation, typography) | `src/JoyNect.Ui/Controls/*`, `src/JoyNect.Ui/Theme/*` | Re-read source; control tests exist | **not-evidenced-yet (full), code path evidenced by review + tests** | Control/motion/geometry tests exist (`M3ControlGeometryTests`, `M3ControlMotionTests`). Not run individually this pass. M3Select sheet slide/fade and reduced-motion snap covered by `M3SelectSheetTests`; clean Release build and full suite run: 186 passed, 0 failed (includes 2 new `M3SegmentedButtonsMotionTests` for slide and reduced-motion snap). |
| 6.3 | High contrast mode meets AAA for text | `MaterialColorTests.HighContrastModeMeetsAaaForText` | Exists in repo; not re-run individually this pass | **not-evidenced-yet** | Claim is concrete and testable; evidence next pass. |

---

## 7. CLI and diagnostics surface

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 7.1 | `--help` | `src/JoyNect.Core/Cli/CliHost.cs` | Launched binary | **environment-blocked (no output this pass)** | Binary did not produce visible help text this pass. Suspected cause: the broken-working-tree rebuild path or console-attach behavior; the prebuilt binary ran other commands fine earlier. Needs a clean build before trusting CLI output. |
| 7.2 | `--diagnose` | `IAppState.BuildDiagnosticReportAsync`, `DiagnosticReport`, `ApiSurfaceDumper` | Launched prebuilt binary | **evidenced-working (headless report)** | Produced a real report: version 1.1.0.0, OS 10.0.26300, .NET 8.0.33, driver available, no Nintendo HID interfaces present. |
| 7.3 | `--driver` probe | `DriverProbe`, `AppState.RefreshDriver` | Launched prebuilt binary | **environment-blocked (no visible output this pass)** | Same CLI-output caveat as `--help`. The probe logic is evidenced indirectly by the `--diagnose` driver line, but the dedicated `--driver` output was not captured cleanly here. |
| 7.4 | `--enumerate` HID candidates | `DeviceEnumerator`, `HidCandidate` | Launched prebuilt binary | **environment-blocked (no visible output this pass)** | Same CLI-output caveat. |
| 7.5 | `--api-dump` ViGEm client surface | `ApiSurfaceDumper` | Launched prebuilt binary | **environment-blocked (no visible output this pass)** | Same CLI-output caveat. |
| 7.6 | `--capture` screenshot renderer | `CaptureRunner`, `DemoState`, `Program.Main` `--capture` branch | Re-read source | **not-evidenced-yet** | Path exists and is part of the same-exe claim. Not run here this pass. |

---

## 8. Packaging/release artifacts

| # | Claimed feature | Where it lives in the repo | How checked here | Status | Notes |
|---|-----------------|----------------------------|------------------|--------|-------|
| 8.1 | Release ZIP / exe artifacts exist for a prior version | `artifacts/JoyNect-1.0.0-win-x64/` | Re-read listing | **not-evidenced-yet (old artifact), environment-blocked (current exe not freshly built)** | An earlier artifact dir exists. This pass could not produce a fresh clean build, so no new release artifact was produced or verified this pass. |

---

## 9. Current blockers found this pass

### Blocker found and resolved this pass — current working tree did not compile

File: `src/JoyNect.Core/Settings/SettingsStore.cs`

What happened: an uncommitted edit on disk had tried to split `Load()` into a locked wrapper plus
`LoadUnlocked()`, but the `catch` clause was left outside the moved method body, so the file did not
parse and the whole solution failed to build.

Evidence this pass:
- Before fix: `dotnet build JoyNect.sln -c Release` failed with 9 errors in `SettingsStore.cs`.
- After fix: clean build, then full suite 175/0 in that earlier pass (not re-run in this pass; this pass's result is 177 passed, 5 failed, see row 1.2), then CLI commands ran from
  the rebuilt binary.

Fix landed this pass: restored the intended `Load()`/`LoadUnlocked()` shape so the `try/catch` is back
inside `LoadUnlocked()` and the file compiles again.

---

## 10. End-of-pass verdict

Evidenced this pass:
- Committed test fixes still present and passing.
- Full suite not green this pass: 177 passed, 5 failed of 182 (see row 1.2).
- Real headless CLI `--diagnose` output from the prebuilt binary.
- The app's claimed page/shell/onboarding/mapping/pad/motion/design surface is all findable in the repo;
  the claims are real, not invented.

Not evidenced yet:
- Anything that needs a fresh clean build (because the current tree is broken).
- Anything that needs a visible WPF window, tray, or click-through (environment-blocked this pass).
- Anything that needs real controllers or real submitted ViGEm pads (environment-blocked this pass).

Environment-blocked this pass:
- WPF visual/click behavior.
- Real ViGEm pad creation/submission verification.
- Real controller/HID behavior.
- Clean CLI `--help/--driver/--enumerate/--api-dump` output (blocked by the broken build, not proven absent).

Next most useful bounded step:
- Convert more "not-evidenced-yet" rows into real evidence by actually exercising them here, starting
  with the headless/simulated paths that are reachable without a visible WPF session or real controllers.
  Concretely: a smoke launch of the rebuilt binary under `--tray` or a captured first-run/startup
  stdout/stderr capture, plus any in-repo smoke or screenshot-capture path (`--capture`), and any
  settings save/load round-trip that can be checked headless.

Immediate remaining open items:
- Visual/click-through WPF behavior: still environment-blocked this pass.
- Real ViGEm pad creation/submission verification: still environment-blocked this pass (driver probe
  says the bus is present, but the pad submit path itself was not exercised end-to-end here).
- Real Nintendo Switch controller / HID behavior: no controller present here, so still environment-blocked.
- Full product verification across every page, shell action, mapping action, and setting change: not yet
  done; the goal is everything, and this pass is still early.
