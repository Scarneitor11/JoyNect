# JoyNect Changelog

All notable changes to this project will be documented in this file.

## [1.2.0] - 2026-10-29

### Added
- **HID Device Enumeration Caching** (`src/JoyNect.Core/Hid/DeviceEnumerator.cs`): 
  - Added `ConcurrentDictionary` cache with `TimeSpan`-based refresh interval
  - Dramatically reduces HID stack enumeration overhead from ~43,200 calls/day to just a few
  - Cache automatically refreshes, handles device add/remove events gracefully
  - Configurable `CacheRefreshInterval` static property

- **New Utility Files**:
  - `HidDevicePath.cs`: HID device path parsing and classification
  - `InputReports.cs`: Input report and IMU calibration types
  - `DiagnosticReport.cs`: Application diagnostic reporting

- **New Enum Types**:
  - `ConnectionKind` (Bluetooth/USB) in `SwitchModel.cs`
  - `SupportLevel` (Verified/Unverified/BestEffort) in `SwitchModel.cs`
  - `JoyConMode` (Sideways/Combined) in `MappingProfile.cs`

- **New Record Types**:
  - `HidDeviceMatch`: Device + candidate pairing
  - `ModelInfo`: Model name + support level + notes
  - `ImuSampleRaw`: Raw IMU counts before calibration
  - `ImuCalibration`: Controller IMU calibration data
  - `ReportInfo`: Input/output report information
  - `FullStateReport`: Complete 49-byte Switch input report
  - `StickSettings`: Analog stick shaping configuration

- **New Interface/Abstraction**:
  - `M3Theme`: Central Material 3 theme with colour roles and typography
  - `M3ThemeManager`: Theme management with system/wallpaper following

- **New Controls** (Material Design 3):
  - Enhanced `M3Button` with full theming, accessibility, icon support
  - Enhanced `M3Card` with elevation, interactive state, theme support

- **Improved XML Documentation**:
  - Comprehensive docs on all public types and members
  - Source references in `ProtocolConstants.cs`
  - Remarks explaining design philosophy

- **Menu rebuild (Material 3)**:
  - New `M3PopupMenu` and `M3PopupSheet` popup surfaces
  - Segmented buttons, select sheet and press ripple controls with motion tests
  - Nav rail entrance animation and tray icon host updates

### Changed
- **`MappingProfile.CreateDefault()`**: Added initial capacity (18) to button dictionary
- **`MappingProfile.Name`**: Added null validation in setter, throws `ArgumentNullException`
- **`MotionPipeline.Convert()`**: Added `ArgumentNullException.ThrowIfNull()` for `raw` and `calibration`
- **`MotionPipeline.EstimateGyroBias()`**: Added `ArgumentNullException.ThrowIfNull()` for `samples`
- **`MotionPipeline.ApplyBias()`**: Added `ArgumentNullException.ThrowIfNull()` for `sample` and `bias`
- **`ControllerManagerOptions`**: Enhanced XML documentation on all properties
- **`AppState`**: Added diagnostic reporting, state snapshots, save/reset operations
- **`DeviceEnumerator`**: Cache-driven enumeration, safe HID device access

### Fixed
- **Task.Delay parameter**: Fixed incorrect `Task.Delay(_options.DiscoveryInterval, _timeProvider, ...)` to standard `Task.Delay(_options.DiscoveryInterval, cancellationToken)`
- **Null safety**: Added `ArgumentNullException.ThrowIfNull()` in `MotionPipeline` methods
- **Descriptor error handling**: `Describe()` returns candidate with `DescriptorError` set on failure
- **Button dictionary**: Proper null validation and initial capacity

### Deprecated
- None

### Removed
- None

## [1.1.0] - 2026-10-15

### Added
- Controller disconnect/reconnect handling
- NSO retro controller support
- Combined Joy-Con mode
- Motion pipeline calibration
- Virtual pad creation options

### Fixed
- HID enumeration performance issues
- Null reference edge cases
- Dictionary initialization patterns

## [1.0.0] - 2026-10-01

### Added
- Initial release with core controller emulation
- HID device enumeration
- ViGEm virtual pad backend
- Basic mapping profiles
- Motion IMU processing

---

*Generated automatically from git tags and commit history.*