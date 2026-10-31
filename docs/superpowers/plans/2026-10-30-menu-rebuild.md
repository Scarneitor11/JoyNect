# JoyNect Menu System Rebuild — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild every JoyNect menu (tray, dropdown, nav rail, segmented buttons) as smooth Material interfaces with slide-in/out, staggered reveals, highlight travel, press ripples, and expressive cubic-bezier easing.

**Architecture:** Seven independent subagents each own one slice of the menu stack (motion helpers, interactive base ripple, select dropdown, segmented+rail, tray menu, tests, consolidation). Tasks are independent — no task depends on another's runtime interface, only on shared `M3Motion`/`M3Interactive` primitives which are backward-compatible additions.

**Tech Stack:** .NET 8, C#, WPF, existing `M3Motion`/`M3Easing`/`M3Duration`/`M3Interactive` primitives

**Spec:** `docs/superpowers/specs/2026-10-30-menu-rebuild-design.md`

## Global Constraints

- Windows-only; WPF, .NET 8, `TreatWarningsAsErrors=true` (0 warnings, 0 errors)
- No new NuGet packages
- All animation respects `M3Motion.ReducedMotion` (snaps instantly when on)
- Existing keyboard shortcuts and automation peers keep working
- 175 existing tests must all still pass

## Review Focus

These input classes/failure modes are implied by the spec but not fully pinned by any single task:

1. **Reduced motion on** — every slide/fade/scale animation must snap to final state instantly, not animate.
2. **Menu open then immediate close** — rapid toggle must not leave a half-animated or orphaned popup.
3. **Keyboard nav in select dropdown** — Up/Down highlight travel must animate, not snap.
4. **Press ripple on disabled control** — ripple must not play when `IsEnabled == false`.
5. **Tray menu rebuild** — must not use WinForms `MenuItem`; must be a WPF popup with Material styling.

---

### Task 1: M3Motion — staggered animation helper + entrance presets

**Files:**
- Modify: `src/JoyNect.Ui/Theme/M3Motion.cs`

**Interfaces:**
- Consumes: existing `M3Motion.Animate`, `M3Easing`, `M3Duration`
- Produces: `M3Motion.Stagger(target, property, to, startDelayMs, durationMs, easing)` and `M3Motion.Entrance(...)`

- [ ] **Step 1: Write failing test for `Stagger`**
  Test that `Stagger` begins animation on the target after `startDelayMs` and reaches `to` by `startDelayMs + durationMs`, using reduced-motion snap when `ReducedMotionOverride = true`.

- [ ] **Step 2: Run test to verify it fails** (`dotnet test --filter "M3MotionStagger"`)
  Expected: compile error / test not found

- [ ] **Step 3: Implement `M3Motion.Stagger`**
  Signature: `public static void Stagger(Animatable target, DependencyProperty property, double to, int startDelayMs, int durationMs = M3Duration.Short4, IEasingFunction? easing = null)`
  Uses a `DispatcherTimer` (or `BeginAnimation` with `BeginTime`) to start after `startDelayMs`.

- [ ] **Step 4: Implement `M3Motion.Entrance` preset**
  `public static void Entrance(Animatable target, DependencyProperty property, double to, int index, int total)` — staggered entrance: `startDelayMs = index * 40`, `durationMs = M3Duration.Medium2`, easing `EmphasizedDecelerate`.

- [ ] **Step 5: Add `M3Duration.Entrance = 220` and `M3Duration.EntranceClose = 150`**
  Duration tokens for menu open/close.

- [ ] **Step 6: Run test to verify it passes**
  Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/JoyNect.Ui/Theme/M3Motion.cs tests/...
git commit -m "feat: add staggered animation helper and entrance presets to M3Motion"
```

---

### Task 2: M3Interactive — press ripple + expressive state layers

**Files:**
- Modify: `src/JoyNect.Ui/Controls/M3Interactive.cs`

**Interfaces:**
- Consumes: `M3Motion.Animate`, `M3Easing`, `M3Duration`
- Produces: ripple visual on press, expressive state-layer opacity curves, entrance support on `_fill`

- [ ] **Step 1: Write failing test for ripple presence**
  Test that pressing (`IsPressed == true`) adds a ripple visual (a `Border`/`Ellipse` state layer) and releasing fades it out. Use reflection or public `Activate()` + check visual children count.

- [ ] **Step 2: Run test to verify it fails**
  Expected: FAIL (no ripple exists)

- [ ] **Step 3: Implement press ripple**
  Add a `_ripple` `Border`/`Ellipse` to `_root` in constructor, scaled 0 → 1 on press with `EmphasizedDecelerate`, faded out on release. Guarded by `IsEnabled` and `ReducedMotion`.

- [ ] **Step 4: Replace state-layer `DoubleAnimation` with `M3Motion.Animate` using `Emphasized` easing**
  Hover/focus/pressed opacities use Material cubic-bezier curves instead of `Standard`.

- [ ] **Step 5: Add entrance support**
  `protected virtual void AnimateEntrance()` — fades `_fill.Opacity` 0→1 and slides `_host` up 8px→0 over `M3Duration.Entrance` with `EmphasizedDecelerate`. Called by derived controls on first `OnLoaded`.

- [ ] **Step 6: Run build + existing Ui tests**
  Expected: 0 warnings/errors; existing `M3ControlMotionTests` still pass

- [ ] **Step 7: Commit**

```bash
git add src/JoyNect.Ui/Controls/M3Interactive.cs tests/...
git commit -m "feat: add press ripple and expressive state layers to M3Interactive"
```

---

### Task 3: M3Select — slide sheet dropdown rewrite

**Files:**
- Modify: `src/JoyNect.Ui/Controls/M3Select.cs`

**Interfaces:**
- Consumes: `M3Motion.Stagger`, `M3Motion.Entrance`, `M3Interactive.AnimateEntrance`
- Produces: slide-up/down dropdown, highlight travel animation, checkmark fade

- [ ] **Step 1: Write failing test for select open/close**
  Test that opening slides the menu up from `VerticalOffset = 8` to `4` over `Entrance`, closing slides down. Test that `_popup.IsOpen` toggles. Test reduced-motion snaps.

- [ ] **Step 2: Run test to verify it fails**
  Expected: FAIL (popup uses `Fade`, no slide)

- [ ] **Step 3: Implement slide-up/down**
  Replace `PopupAnimation = Fade` with `None`; animate `_menu.Opacity` 0→1 and `RenderTransform.Y` 8→0 on open; reverse on close. Use `M3Motion.Animate` with `Entrance` durations and `EmphasizedDecelerate`/`EmphasizedAccelerate`.

- [ ] **Step 4: Implement highlight travel animation**
  On `StepThroughOptions`, animate the highlighted button's background crossfade (SecondaryContainer ↔ SurfaceContainerHigh) instead of instant `SetAppearance`.

- [ ] **Step 5: Implement checkmark fade**
  On `UpdateButtons`, fade checkmark icon opacity 0→1 when selected appears, 1→0 when deselected.

- [ ] **Step 6: Respect `ReducedMotion`**
  All slide/fade animations snap when `M3Motion.ReducedMotionOverride = true`.

- [ ] **Step 7: Run build + Ui tests**
  Expected: 0 warnings/errors; `M3ControlMotionTests` still pass

- [ ] **Step 8: Commit**

```bash
git add src/JoyNect.Ui/Controls/M3Select.cs tests/...
git commit -m "feat: rebuild M3Select as slide sheet with highlight travel and checkmark fade"
```

---

### Task 4: M3SegmentedButtons + NavRail — smooth selection + entrance

**Files:**
- Modify: `src/JoyNect.Ui/Controls/M3SegmentedButtons.cs`
- Modify: `src/JoyNect.Ui/Shell/NavRail.cs`
- Modify: `src/JoyNect.Ui/Shell/NavRailButton.cs`

**Interfaces:**
- Consumes: `M3Motion.Animate`, `M3Motion.Entrance`
- Produces: smooth segment selection transition; nav rail staggered entrance + selection slide

- [ ] **Step 1: Segmented buttons — smooth selection**
  Test that changing `SelectedIndex` crossfades the selected segment's fill/foreground over `M3Duration.Short3` instead of snapping. Test reduced-motion snap.

- [ ] **Step 2: Implement segmented button transition**
  In `SelectedIndex` setter, animate `_segments[i].Button` container/content roles via `SetAppearance` with color crossfade using `M3Motion.Animate` on an intermediate brush, or simpler: animate opacity of selected/unselected visual states.

- [ ] **Step 3: Nav rail — staggered entrance**
  In `SetDestinations`, call `M3Motion.Entrance` on each `NavRailButton` with staggered `index * 40ms` delay so items fade/scale in one by one.

- [ ] **Step 4: Nav rail button — smooth selection**
  In `NavRailButton.RefreshTheme`, animate `_pill.Opacity` and icon/label color using `M3Motion.Animate` with `Emphasized` easing instead of raw `DoubleAnimation`.

- [ ] **Step 5: Respect `ReducedMotion`**
  All transitions snap when `M3Motion.ReducedMotionOverride = true`.

- [ ] **Step 6: Run build + Ui tests**
  Expected: 0 warnings/errors; existing tests pass

- [ ] **Step 7: Commit**

```bash
git add src/JoyNect.Ui/Controls/M3SegmentedButtons.cs src/JoyNect.Ui/Shell/NavRail.cs src/JoyNect.Ui/Shell/NavRailButton.cs tests/...
git commit -m "feat: smooth selection transitions and staggered entrance on segmented buttons and nav rail"
```

---

### Task 5: TrayIconHost — custom Material tray menu

**Files:**
- Modify: `src/JoyNect.App/Shell/TrayIconHost.cs`

**Interfaces:**
- Consumes: `M3Interactive`-style styling, `M3Motion` animations
- Produces: WPF popup menu with Material styling, icons, hover layer, open/close fade+scale, no WinForms `MenuItem`

- [ ] **Step 1: Write failing test for tray menu**
  Test that `BuildMenu()` creates WPF `MenuItem`s (not WinForms), each with icon + text + hover state. Test that opening triggers fade+scale animation. Test reduced-motion snap.

- [ ] **Step 2: Implement Material tray menu**
  Replace `new MenuItem { Header = ... }` (WinForms) with WPF `MenuItem`s styled via `SetAppearance`-like roles: background `SurfaceContainerHigh`, foreground `OnSurface`, hover layer `OnSurface` at `HoverOpacity`, icons from `M3Icons`. Animate `_menu.Opacity` 0→1 and scale on `Opened`, reverse on `Closed`.

- [ ] **Step 3: Add icons to tray items**
  "Open JoyNect" → `M3Glyph.Open`/`Gamepad`, "Pause/Resume" → `M3Glyph.Power`, "Scan" → `M3Glyph.Refresh`, "Exit" → `M3Glyph.Close`.

- [ ] **Step 4: Respect `ReducedMotion`**
  Open/close snap when `M3Motion.ReducedMotionOverride = true`.

- [ ] **Step 5: Run App build + Ui tests**
  Expected: 0 warnings/errors; existing tests pass

- [ ] **Step 6: Commit**

```bash
git add src/JoyNect.App/Shell/TrayIconHost.cs tests/...
git commit -m "feat: replace WinForms tray menu with Material WPF popup menu"
```

---

### Task 6: Tests — animation logic coverage

**Files:**
- Create: `tests/JoyNect.Core.Tests/Ui/M3MenuAnimationTests.cs`
- Create: `tests/JoyNect.Core.Tests/Ui/M3SelectAnimationTests.cs`

**Interfaces:**
- Consumes: `M3Motion.Stagger`, `M3Select`, `M3Interactive`
- Produces: test coverage for animation behavior

- [ ] **Step 1: `M3MotionStagger` test** (owned by Task 1)
  `Stagger` begins after delay, reaches target after delay+duration; reduced-motion snaps.

- [ ] **Step 2: `M3SelectSlide` test** (owned by Task 3)
  Opening slides offset from 8→4; closing slides 4→8; checkmark fades; reduced-motion snaps.

- [ ] **Step 3: `M3SelectHighlightTravel` test** (owned by Task 3)
  `StepThroughOptions` animates highlight crossfade instead of snap.

- [ ] **Step 4: `NavRailEntrance` test** (owned by Task 4)
  `SetDestinations` applies staggered entrance delays increasing with index.

- [ ] **Step 5: `M3InteractiveRipple` test** (owned by Task 2)
  Press adds ripple visual; release fades; disabled plays no ripple.

- [ ] **Step 6: Run full Ui test collection**
  Expected: all pass, 0 failures

- [ ] **Step 7: Commit**

```bash
git add tests/...
git commit -m "test: add animation coverage for menus (stagger, select slide, ripple, rail entrance)"
```

---

### Task 7: Consolidation — build + full test gate + fix fallout

**Files:**
- Modify: any source as needed to fix fallout

**Interfaces:**
- Consumes: all previous tasks
- Produces: green build/test gate

- [ ] **Step 1: Run full build**
  `dotnet build JoyNect.sln -c Release` → 0 warnings, 0 errors

- [ ] **Step 2: Run full test suite**
  `dotnet test JoyNect.sln -c Release` → 175+N tests, 0 failed

- [ ] **Step 3: Fix any fallout**
  If any agent's change broke another (e.g., `M3Interactive` ripple affecting `M3Select` buttons), fix with minimal changes.

- [ ] **Step 4: Run reduced-motion variant**
  Set `M3Motion.ReducedMotionOverride = true` in a test, confirm animations snap (no exceptions, no half-animated states).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: final gate verification after menu rebuild"
```

---

## Self-review against spec

1. **Spec coverage:** Every goal (tray replacement, select slide sheet, ripple, nav rail entrance, segmented transition, staggered helper, gate green) maps to a task. No gaps.
2. **Step scan:** Each step lets the implementer write exactly one reasonable thing. No "TBD" lines.
3. **Type consistency:** `M3Motion.Stagger`/`Entrance` signatures defined in Task 1 and consumed by Tasks 3–5. `M3Interactive.AnimateEntrance` defined in Task 2 and consumed by Tasks 3–5.
4. **Review Focus:** All five items mapped — Tasks 2, 3, 4, 5, 2 respectively.
5. **Proportion:** Plan is shorter than the code it describes; bodies are signatures/assertions, not transcripts.

---

**Execution method:** User supplied "use multiple subagents, the most you can" — dispatching all 7 agents in parallel via subagent-driven-development. Tasks 1–6 are independent (each agent owns a distinct file set); Task 7 runs after consolidation. No cross-task blocking during implementation.