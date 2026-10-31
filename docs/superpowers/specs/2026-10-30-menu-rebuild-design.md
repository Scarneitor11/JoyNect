# JoyNect Menu System Rebuild — Design Doc

Date: 2026-10-30
Repo: C:\Projects\JoyNect (mirrors https://github.com/Scarneitor11/JoyNect)
Approach selected: B — full Material menu system rebuild

---

## Intent and why

JoyNect's menus are inconsistent and feel dated:

- **Tray context menu** (`TrayIconHost.cs`) is a Windows Forms `MenuItem` — looks like Windows 95, no Material styling, no hover state, no icons, no open/close animation.
- **Dropdown / select** (`M3Select.cs`) opens with a basic `Popup.Fade`, no slide-in/out, highlight snaps instantly, checkmark appears/disappears without animation.
- **Navigation rail** (`NavRail.cs`) uses raw `DoubleAnimation` instead of `M3Motion`, no entrance animation, selection switches instantly.
- **Segmented buttons** (`M3SegmentedButtons.cs`) — selection switches instantly, no transition between segments.
- **Base interactive** (`M3Interactive.cs`) — state layer uses standard `DoubleAnimation` instead of expressive Material cubic-bezier curves; no press ripple, no entrance support.

The goal: every menu interaction should feel smooth, intentional, and "Material" — slide-ins, staggered item reveals, highlight travel, press ripples, and expressive easing everywhere.

---

## Goals

1. Replace the WinForms tray menu with a custom Material-styled WPF popup menu (icons, hover layer, icons, smooth open/close fade + scale).
2. Rebuild `M3Select` dropdown as a slide sheet — slide-up/down on open/close, highlight travel animation on keyboard nav, checkmark fade.
3. Add press ripple effect to all menu-interactive controls (buttons, select options, segmented buttons, rail buttons).
4. Add entrance animations to nav rail items (staggered fade/scale on first build).
5. Add smooth selection transitions to segmented buttons (selected segment slides/fades between segments).
6. Add a staggered animation helper to `M3Motion` for menu item groups.
7. Keep the build gate green: `dotnet build` 0 warnings/0 errors; `dotnet test` all pass.

---

## Non-goals

- Changing the navigation structure (rail stays a rail, tray stays a tray).
- Adding new menu destinations or controls beyond animation/styling.
- Changing color roles or theme generation.
- Supporting reduced-motion off (already exists; animations snap when `ReducedMotion` is on).

---

## Anti-goals (failure even if technically works)

- Menus that look Material but stutter or lag on open/close.
- Animations that fight layout (e.g., sliding a menu that causes re-measure during the tween).
- Breaking keyboard navigation or screen-reader semantics.
- Regressions in the 175 existing tests.

---

## Constraints

- Windows-only; WPF, .NET 8, `TreatWarningsAsErrors=true`.
- No new NuGet packages.
- All animation must respect `M3Motion.ReducedMotion` (snap instantly when on).
- All existing keyboard shortcuts and automation peers must keep working.
- Tests run headless on STA threads; any new testable animation logic must not require a visible window.

---

## Files to create / modify

| File | Action | Responsibility |
|------|--------|----------------|
| `src/JoyNect.Ui/Theme/M3Motion.cs` | Modify | Add `Stagger` helper and `Entrance` duration/easing presets |
| `src/JoyNect.Ui/Controls/M3Interactive.cs` | Modify | Press ripple effect on `_stateLayer`; expressive cubic-bezier state layers; entrance animation support |
| `src/JoyNect.Ui/Controls/M3Button.cs` | Modify | Expose ripple toggle; ensure press/release uses ripple |
| `src/JoyNect.Ui/Controls/M3Select.cs` | Rewrite | Slide-up/down sheet instead of `Popup.Fade`; highlight travel; checkmark fade; keyboard highlight travel animation |
| `src/JoyNect.Ui/Controls/M3SegmentedButtons.cs` | Modify | Smooth segment selection transition (slide background or color crossfade) |
| `src/JoyNect.Ui/Shell/NavRail.cs` + `NavRailButton.cs` | Modify | `M3Motion`-based selection animation; staggered entrance on `SetDestinations` |
| `src/JoyNect.App/Shell/TrayIconHost.cs` | Rewrite menu | Custom Material WPF popup menu (no WinForms `MenuItem`); icons; hover layer; open/close fade+scale |
| `tests/.../Ui/` | Add | Tests for staggered animation, select open/close timing, ripple presence |

---

## The plan (as decided)

Seven parallel subagents (per "use multiple subagents, the most you can"):

| Agent | Mandate |
|-------|---------|
| A1 | `M3Motion.cs` — staggered animation helper + entrance presets |
| A2 | `M3Interactive.cs` — press ripple + expressive state layers |
| A3 | `M3Select.cs` — slide sheet dropdown rewrite |
| A4 | `M3SegmentedButtons.cs` + `NavRail*` — smooth selection + entrance animations |
| A5 | `TrayIconHost.cs` — custom Material tray menu |
| A6 | Tests — new UI tests for animation logic |
| A7 | Consolidation — build + full test gate, fix any fallout |

After agents report: consolidate, run build + tests, fix fallout, commit.

---

## What's left to the builder (this doc)

- Whether to ripple on *all* buttons or only filled/tonal (default: all interactive variants).
- Whether tray menu should show controller count as a submenu (default: no, keep flat).
- Exact duration numbers for slide-up/down (default: 220ms emphasized decelerate open, 150ms accelerate close).

---

End of design doc. Proceeding to writing-plans and execution per your "do as you wish" direction.