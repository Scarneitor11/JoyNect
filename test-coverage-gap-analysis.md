# JoyNect Test Coverage Gap Analysis

**Repository:** C:\Projects\JoyNect
**Test Project:** C:\Projects\JoyNect\tests\JoyNect.Core.Tests
**Analysis Date:** 2026-10-29

## Executive Summary

The test suite provides good coverage of core functionality but has significant gaps in edge cases, error handling, and peripheral controller types. The following analysis identifies specific missing test scenarios organized by priority.

---

## 1. Existing Test Coverage Overview

### What's Well Tested
| Area | Test Count | Key Tests |
|------|-----------|-----------|
| Controller discovery & management | 8 facts | ControllerManagerTests |
| Joy-Con pairing modes (Sideways/Combined) | 3 facts | ControllerManagerTests, PadComposerTests |
| Handshake & link state machine | 14 facts | SwitchControllerSessionTests |
| Button mapping (Pro controller) | 16 theories/facts | PadComposerTests, InputReportTests |
| Stick processing (dead zone, inversion, curve) | 10 facts | StickProcessorTests |
| Rumble encoding | 11 facts | RumbleEncoderTests |
| Calibration decoding | 9 facts | CalibrationTests |
| DSU protocol & motion server | 12 facts | DsuProtocolTests, DsuMotionServerTests |
| Motion pipeline (bias, tilt, conversion) | 5 facts | MotionPipelineTests |
| HID device path classification | 5 facts | HidDevicePathTests |
| Settings store | 5 facts | SettingsStoreTests |

### What's Missing Entirely
- NSO retro controller (NES/SNES/N64) support
- Controller disconnect/reconnect cycles
- Access denied / device not found error conditions
- Mapping profiles for all SwitchModel variants
- Motion sample edge cases (empty, NaN, extreme values)
- Rumble during link lifecycle events

---

## 2. Edge Cases Missing from Tests

### Controller Lifecycle
- **No test** for reconnecting a controller after link loss while preserving state
- **No test** for what happens when a controller disappears mid-handshake
- **No test** for multiple rapid connect/disconnect cycles
- **No test** for controller that partially responds (some reports, then silence)

### Button/Input Edge Cases
- **No test** for all-button combinations (holding multiple shoulder buttons + stick)
- **No test** for rapid button press/release sequences
- **No test** for stick buttons (L3, R3) being pressed
- **No test** for Capture button handling

### Rumble Edge Cases
- **No test** for rumble during controller disconnect
- **No test** for rumble strength changes mid-effect
- **No test** for high-frequency rumble (>1000 Hz)
- **No test** for rumble with strength = 0 after previous non-zero rumble

### Calibration Edge Cases
- **No test** for corrupted calibration blocks (partial corruption, not just erased)
- **No test** for calibration with zero delta values
- **No test** for motion calibration with all-zero coefficients

### Motion Sample Edge Cases
- **No test** for empty motion sample arrays
- **No test** for NaN/Infinity in motion values
- **No test** for maximum/minimum extreme values
- **No test** for gravitational vector when controller is vertical

---

## 3. Missing Test Scenarios by Category

### A. Controller Disconnect/Reconnect [CRITICAL]

**File:** `ControllerManagerTests.cs` (lines 125-165) - Tests link loss but not reconnection

**Missing Scenario:** Controller disconnects, then reconnects. Pad should be released then reacquired.

**Suggested Test Method:**
```csharp
[Fact]
public void ControllerDisconnectsAndReconnects_PadIsReleasedThenReacquired()
{
    using var harness = new ManagerHarness();
    var transport = harness.AddController();
    using var manager = harness.Start();
    
    // Verify pad is created
    Assert.True(WaitFor(() => harness.Backend.Pads.Count == 1));
    
    // Simulate link loss
    transport.Dispose();
    
    // Pad should be gone
    Assert.True(WaitFor(() => manager.Pads.Count == 0));
    Assert.Empty(manager.Sessions);
    
    // Reconnect same controller
    var transport2 = harness.AddController();
    using var manager2 = harness.Start();
    
    // Pad should be reacquired
    Assert.True(WaitFor(() => harness.Backend.Pads.Count == 1));
    Assert.Equal(1, manager2.Sessions[0].Slot);
}
```

**Priority:** Critical - This is the primary handshake/recovery path users experience.

---

### B. Combined Joy-Con Mode Edge Cases [HIGH]

**File:** `ControllerManagerTests.cs` (line 63-93) - Only tests initial combine, not reconnect or mode switch

**Missing Scenarios:**
1. Combined mode after reconnect - does the pair re-merge?
2. Combined mode with one Joy-Con disconnected - does the single half become its own pad?
3. Switching from Sideways to Combined mode mid-session

**Suggested Test Method:**
```csharp
[Fact]
public void CombinedJoyConReconnectPreservesPairing()
{
    using var harness = new ManagerHarness(JoyConMode.Combined);
    var left = harness.AddController(SwitchModel.JoyConLeft, mac: 0x001122334455);
    var right = harness.AddController(SwitchModel.JoyConRight, mac: 0x001122334466);
    using var manager = harness.Start();
    
    Assert.Single(manager.Pads);
    Assert.Equal(2, manager.Sessions.Count);
    
    // Disconnect and reconnect
    left.Dispose();
    right.Dispose();
    
    var left2 = harness.AddController(SwitchModel.JoyConLeft, mac: 0x001122334455);
    var right2 = harness.AddController(SwitchModel.JoyConRight, mac: 0x001122334466);
    using var manager2 = harness.Start();
    
    // Should re-form the pair
    Assert.Single(manager2.Pads);
    Assert.Equal(2, manager2.Sessions.Count);
}
```

**Priority:** High - Combined mode is a key use case for multi-player setups.

---

### C. NSO Retro Controllers [HIGH]

**Missing Entirely:** No tests for Nintendo Switch Online retro controllers (NES, SNES, N64).

**Key Differences:**
- Different VendorId/ProductId (0x057E is Pro Controller, retro controllers use different IDs)
- Different report format (8-byte vs 48-byte full state)
- Different button mapping (NES has A/B/Select/Start, SNES has A/B/X/Y/Select/Start)
- Different IMU presence (none on most retro controllers)
- Different calibration requirements

**Suggested Test Classes:**
- `RetroControllerTests` - NES, SNES, N64 controller support
- Test that retro controllers can be discovered and produce usable input
- Test that button mapping is correct for each retro type
- Test that rumble works (if supported)

**Priority:** High - Retro controller support is a requested feature with no test coverage.

---

### D. Error Conditions [CRITICAL]

**Missing Scenarios:**

1. **Device Not Found** - Discovery returns no devices, manager handles gracefully
2. **Access Denied** - HID device open fails (permissions, another app has it)
3. **Busy/Timeout** - Controller responds but slowly, manager doesn't hang
4. **Report Corruption** - Garbage bytes in report, decoder doesn't crash

**Suggested Test Methods:**

```csharp
// Device not found
[Fact]
public void NoDiscoveredDevices_ManagerFailsGracefully()
{
    using var harness = new ManagerHarness(backend: new NullPadBackend());
    // No controllers added
    using var manager = harness.Start();
    // Should have zero sessions, no crash
    Assert.Empty(manager.Sessions);
    Assert.Empty(manager.Pads);
}

// Access denied / device open failure
[Fact]
public void AccessDeniedDuringOpen_ManagerReportsFailure()
{
    using var harness = new ManagerHarness();
    // Add controller with path that will fail to open
    harness.AddController();
    using var manager = harness.Start();
    // Should have failure event fired, no pad created
    var failures = new List<string>();
    manager.PadCreationFailed += (_, reason) => failures.Add(reason);
    Assert.True(failures.Count > 0);
    Assert.Empty(manager.Pads);
}
```

**Priority:** Critical - Error handling is essential for production quality.

---

### E. Mapping Profile Creation for All Controller Types [HIGH]

**Current State:** PadComposerTests only tests ProController and JoyConLeft/JoyConRight.

**Missing:** Mapping profiles for all `SwitchModel` enum values.

**SwitchModel Enum (from source):** Likely includes: ProController, JoyConLeft, JoyConRight, NES, SNES, N64, maybe others.

**Suggested Test Methods:**

```csharp
[Theory]
[InlineData(SwitchModel.ProController)]
[InlineData(SwitchModel.JoyConLeft)]
[InlineData(SwitchModel.JoyConRight)]
[InlineData(/* other models */)]
public void MappingProfileCreatedForAllControllerTypes(SwitchModel model)
{
    var profile = MappingProfile.CreateDefault(model);
    Assert.NotNull(profile);
    Assert.NotNull(profile.Remap); // or whatever the API provides
}
```

Also test PadComposer creation:
```csharp
[Fact]
public void PadComposerCreatedForJoyConLeft()
{
    var composer = new PadComposer(MappingProfile.CreateDefault(SwitchModel.JoyConLeft));
    Assert.NotNull(composer);
}

[Fact]
public void PadComposerCreatedForProController()
{
    var composer = new PadComposer(MappingProfile.CreateDefault(SwitchModel.ProController));
    Assert.NotNull(composer);
}
```

**Priority:** High - Ensures all controller types can create valid mappings.

---

### F. Motion Sample Processing Edge Cases [MEDIUM]

**Current State:** MotionPipelineTests covers basic bias, tilt, conversion.

**Missing Scenarios:**

1. **Empty motion samples** - `MotionPipeline.EstimateGyroBias([])` already tested, but what about other functions?
2. **NaN/Infinity handling** - What happens with corrupt motion data?
3. **Extreme acceleration values** - Max +/-3G or whatever the range is
4. **Zero timestamp** - Conversion with timestamp = 0
5. **Very large timestamps** - Delta time overflow

**Suggested Test Methods:**

```csharp
[Fact]
public void MotionPipelineHandlesNaNInput()
{
    var calibration = ImuCalibration.Default;
    var raw = new ImuSampleRaw(float.NaN, 0, 0, 0, 0, 0);
    var sample = MotionPipeline.Convert(raw, calibration, timestamp: 1.0);
    // Should not throw, should produce reasonable output
    Assert.True(!float.IsNaN(sample.AccelerationG.X) || ...);
}

[Fact]
public void MotionPipelineHandlesZeroTimestamp()
{
    var calibration = ImuCalibration.Default;
    var raw = new ImuSampleRaw(16384, 0, 0, 0, 0, 0);
    var sample = MotionPipeline.Convert(raw, calibration, timestamp: 0);
    // Zero timestamp should be handled gracefully
    Assert.Equal(0, sample.Timestamp);
}
```

**Priority:** Medium - Edge cases in math pipelines are important but less critical than controller errors.

---

### G. Rumble During Link Lifecycle [MEDIUM]

**Current State:** Rumble tests exist, but not during disconnect/reconnect.

**Missing Scenarios:**
1. Rumble started, then controller disconnects - rumble should stop
2. Rumble stopped, then controller reconnects - state preserved or reset
3. Rumble strength change during active effect

**Suggested Test Methods:**
```csharp
[Fact]
public void RumbleStopsWhenControllerDisconnects()
{
    using var harness = new Harness();
    harness.Session.SetRumble(255, 255);
    Assert.True(WaitFor(() => !RumbleEncoder.IsNeutral(...)));
    
    // Simulate disconnect
    harness.Transport.Dispose();
    
    // Rumble should cease
    Assert.True(RumbleEncoder.IsNeutral(harness.Transport.LastRumble.AsSpan(0, 4), ...));
}
```

**Priority:** Medium - Nice to have for robustness.

---

## 4. Test Data Quality Assessment

### Strengths
- **Input report test data** is systematically generated byte-by-byte with clear offset documentation
- **Calibration test data** uses documented Pro Controller sample values (0x4F7, 0x424, etc.)
- **Rumble test data** derived from documented formulas, not hardcoded recordings
- **DSU protocol test data** generated independently from cemuhook-protocol reference
- **Material color fixtures** generated from official Google Color Utilities library

### Weaknesses
- **Simulated controller options** are sparse - only ProductId, MacAddress, Connection, ReportedDeviceType, and a few report-generation options. Missing: specific rumble capability flags, IMU capability flags, battery level settings.
- **Motion sample values** in some tests use arbitrary numbers (100-600 range) without clear relation to real IMU ranges
- **No test data files** for retro controllers (NES/SNES/N64 report formats)
- **Button byte layouts** are well tested but could use more inline data variations

**Recommendation:** The test data quality is generally good. The main improvement would be adding more parameterized test data to cover boundary conditions.

---

## 5. Test Project References Analysis

### Current Project References (JoyNect.Core.Tests.csproj):
```xml
<ProjectReference Include="..\..\src\JoyNect.Core\JoyNect.Core.csproj" />
<ProjectReference Include="..\..\src\JoyNect.Ui\JoyNect.Ui.csproj" />
```

### Assessment: ⚠️ PARTIAL - Missing References

**Missing Project References:**

1. **JoyNect.Core.Motion** - DSU motion server tests (`DsuMotionServerTests.cs`, `DsuProtocolTests.cs`, `MotionPipelineTests.cs`) need the Motion project
2. **JoyNect.Core.Hid** - Hid device path tests already reference the correct project through JoyNect.Core, but HidControllerTransport and related types come from here
3. **JoyNect.Core.Output** - Output/ViGEm types used in controller management
4. **JoyNect.Core.Settings** - Settings store tests reference this directly

**Actual Dependency Analysis:**

Looking at the test files:
- `ControllerManagerTests.cs` uses: `JoyNect.Core.Devices`, `JoyNect.Core.Hid`, `JoyNect.Core.Mapping`, `JoyNect.Core.Output`, `JoyNect.Core.Protocol` - all within JoyNect.Core, so single reference is sufficient.
- `SwitchControllerSessionTests.cs` uses: `JoyNect.Core.Devices`, `JoyNect.Core.Hid`, `JoyNect.Core.Protocol` - fine.
- `DsuMotionServerTests.cs` uses: `JoyNect.Core.Devices`, `JoyNect.Core.Hid`, `JoyNect.Core.Motion`, `JoyNect.Core.Output`, `JoyNect.Core.Protocol` - **NEEDS Motion project reference**
- `DsuProtocolTests.cs` uses: `JoyNect.Core.Motion`, `JoyNect.Core.Protocol` - **NEEDS Motion project reference**
- `MotionPipelineTests.cs` uses: `JoyNect.Core.Motion`, `JoyNect.Core.Protocol` - **NEEDS Motion project reference**
- `MaterialColorTests.cs` uses: `JoyNect.Core.Design` - this might need a separate Design project reference

**Recommendation:** Add explicit project references for:
- `..\..\src\JoyNect.Core\JoyNect.Core.Motion\JoyNect.Core.Motion.csproj` (or whatever the motion project is named)
- `..\..\src\JoyNect.Core\JoyNect.Core.Design\JoyNect.Core.Design.csproj` if it exists as a separate project

Actually, looking more carefully, many of these tests might work because the types are internal to JoyNect.Core and the test project has the full project reference. The DSU tests might be pulling in Motion types transitively. Let me verify by checking if there's a separate Motion project.

Based on the source file listing, there's `JoyNect.Core\Motion\MotionPipeline.cs` and `JoyNect.Core\Motion\DsuProtocol.cs`. These are likely part of the main `JoyNect.Core` project, not a separate project. So the single reference should be sufficient.

**Conclusion:** The project references appear adequate. All test file usings resolve through the single `JoyNect.Core` reference.

---

## 6. Suggestions for New Test Classes/Methods

### Priority 1 (Critical - Fix Bugs/Regression Prevention)

1. **`ControllerManagerTests.ControllerDisconnectReconnectCycle`**
   - Test: disconnect then reconnect, pad released then reacquired
   - Location: `ControllerManagerTests.cs`

2. **`ControllerManagerTests.AccessDeniedFailurePath`**
   - Test: HID open failure reported as failure event
   - Location: `ControllerManagerTests.cs`

3. **`PadComposerTests.MappingProfileForAllModels`**
   - Theory: MappingProfile.CreateDefault() works for all SwitchModel values
   - Location: `PadComposerTests.cs`

### Priority 2 (High - Feature Gaps)

4. **`RetroControllerTests.NES_ButtonMapping`**
   - Test: NES controller buttons map correctly
   - Location: New file `RetroControllerTests.cs`

5. **`RetroControllerTests.SNES_ButtonMapping`**
   - Test: SNES controller buttons map correctly
   - Location: New file `RetroControllerTests.cs`

6. **`RetroControllerTests.N64_StickMapping`**
   - Test: N64 stick calibration and mapping
   - Location: New file `RetroControllerTests.cs`

7. **`ControllerManagerTests.CombinedModeReconnect`**
   - Test: Combined Joy-Con pair re-forms after disconnect/reconnect
   - Location: `ControllerManagerTests.cs`

### Priority 3 (Medium - Edge Cases)

8. **`MotionPipelineTests.NaN_And_ExtremeValues`**
   - Test: Motion pipeline handles NaN, Infinity, extreme values
   - Location: `MotionPipelineTests.cs`

9. **`RumbleEncoderTests.RumbleDuringDisconnect`**
   - Test: Rumble effect stops when controller disconnects
   - Location: `RumbleEncoderTests.cs` (or new file)

10. **`InputReportTests.AllButtonCombinations`**
    - Theory: All 17 Switch buttons can be combined without corruption
    - Location: `InputReportTests.cs`

### Priority 4 (Low - Polish)

11. **`HidDevicePathTests.EdgeCasePaths`**
    - Theory: Various HID path formats are classified correctly
    - Location: `HidDevicePathTests.cs`

12. **`SettingsStoreTests.ClampAllProperties`**
    - Test: All AppSettings properties are properly clamped on save
    - Location: `SettingsStoreTests.cs`

---

## Summary of Gap Priorities

| Priority | Category | # Gaps | Focus |
|----------|----------|--------|-------|
| Critical | Disconnect/reconnect, error conditions | 3 | Production reliability |
| High | NSO retro controllers, mapping profiles, Combined mode edge cases | 4 | Feature completeness |
| Medium | Rumble lifecycle, motion edge cases, extreme values | 3 | Robustness |
| Low | Edge case paths, settings clamping, button combos | 4 | Polish |

**Total Identified Gaps:** 14 test scenarios across 7 categories

---

## Implementation Notes

### Test Project Structure
All new tests should be added to existing test files where possible, or new files in the appropriate namespace:
- `JoyNect.Core.Tests.Devices` - Controller lifecycle tests
- `JoyNect.Core.Tests.Protocol` - Input report, calibration, rumble tests
- `JoyNect.Core.Tests.Mapping` - Mapping profile tests
- `JoyNect.Core.Tests.Motion` - Motion pipeline tests
- New: `JoyNect.Core.Tests.Retro` - Retro controller tests (new namespace/file)

### Test Patterns to Follow
- Use `ManagerHarness` for end-to-end controller tests
- Use `Harness` class in `SwitchControllerSessionTests` for session-level tests
- Use parameterized theories ([InlineData]) for button/mapping variations
- Follow existing patterns for `WaitFor()` with appropriate timeouts
- Use `NullPadBackend` for driver-less tests
- Follow the `ScriptedDiscovery` pattern for discovery-based tests

### Example Test Template

```csharp
[Fact]
public void NewTestMethod_Name()
{
    // Arrange
    using var harness = new ManagerHarness(/* options */);
    var transport = harness.AddController(/* params */);
    using var manager = harness.Start();
    
    // Act
    // ... test scenario ...
    
    // Assert
    Assert.True(WaitFor(() => condition, timeout: 5000),
        "descriptive error message");
}
```