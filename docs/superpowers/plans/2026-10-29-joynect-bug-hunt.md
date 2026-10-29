# JoyNect Bug Hunt — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Find every bug in JoyNect and fix them so `dotnet build JoyNect.sln -c Release` is clean (0 warnings, 0 errors) and `dotnet test JoyNect.sln -c Release` passes all tests with 0 failures.

**Architecture:** Five parallel subagents each audit a narrow slice of the codebase (code review, security, test reproduction, dependency risk, static analysis). Findings are consolidated into one bug list, then each confirmed bug is fixed with a minimal change. The build/test gate is re-run after fixes.

**Tech Stack:** .NET 8, C#, WPF, xunit, HidSharp, Nefarius.ViGEm.Client, CommunityToolkit.Mvvm, Hardcodet.NotifyIcon.Wpf

**Spec:** `docs/superpowers/specs/2026-10-29-joynect-bug-hunt-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true` — any new warning fails the build
- Windows-only; WPF, HID stack, ViGEmBus; tests run headless against simulated controllers
- No FluentAssertions; xunit only
- The `.env.release` GitHub PAT must never be committed or exposed
- No new NuGet packages unless a fix strictly requires one

## Review Focus

These are the input classes/failure modes the spec implies that no single task's tests fully pin — each gets its test added to the owning task:

1. **ControllerManager pad-slot indexing race** — a pad is looked up by index `[0]` before a different collection is populated; expect the indexed collection to be the one waited on.
2. **SwitchControllerSession report-decoding visibility** — `StateUpdated` may not fire after an input change because cross-thread fields lack volatile/locking visibility.
3. **Hardcoded secret in `.env.release`** — a GitHub PAT is present on disk; `.gitignore` ignores `.env` but not `.env.release`.
4. **ViGEmBus driver absence** — no driver means `CreatePad` returns null; expect controllers still readable, games still unable to see a pad, and a clear remediation message.
5. **AppState.Mapping inconsistency** — returns `Pads[0].Profile` only, while `Remap()` writes to every pad; expect consistent behavior.

---

### Task 1: Security — remove hardcoded GitHub PAT and ignore `.env.release`

**Files:**
- Modify: `.gitignore` — add `.env.release` and `.env.*` patterns
- Modify: `.env.release` — strip the `GITHUB_TOKEN` value (leave a placeholder or empty file)
- Create: none

**Interfaces:**
- Consumes: none
- Produces: none (settings/security change only)

- [ ] **Step 1: Confirm `.env.release` contains the PAT**
  Read `.env.release`; verify it contains `GITHUB_TOKEN=github_pat_...`

- [ ] **Step 2: Add `.env.release` / `.env.*` to `.gitignore`**
  Add `.env.release` and `.env.*` next to the existing `.env` entry so the secret can never be committed.

- [ ] **Step 3: Strip the token from `.env.release`**
  Replace the real PAT with a commented placeholder or empty the value, so the file remains but is safe.

- [ ] **Step 4: Verify `git status --short` shows `.gitignore` and `.env.release` as modified, and no secret text is staged**

- [ ] **Step 5: Commit**

```bash
git add .gitignore .env.release
git commit -m "sec: ignore .env.release and strip hardcoded GitHub PAT"
```

---

### Task 2: Code review — reproduce and fix failing ControllerManager test

**Files:**
- Modify: `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs`

**Interfaces:**
- Consumes: `ControllerManager`, `RecordingBackend`, `ManagerHarness`, `SimulatedControllerTransport`
- Produces: a corrected `CombinesAJoyConPairIntoOnePadWithBothSticks` test

- [ ] **Step 1: Run the failing test in isolation to reproduce**
  Run: `dotnet test JoyNect.sln -c Release --filter "FullyQualifiedName~CombinesAJoyConPairIntoOnePadWithBothSticks"`
  Expected: FAIL with `ArgumentOutOfRangeException` at `ControllerManagerTests.cs:72`

- [ ] **Step 2: Root-cause the index-out-of-range**
  Confirm the test waits on `harness.Backend.Pads.Count == 1` then indexes `manager.Pads[0]` before the manager's collection is populated.

- [ ] **Step 3: Fix the test**
  - Change the readiness wait to `manager.Pads.Count == 1` before indexing `manager.Pads[0]`
  - Change the later pad lookup from `harness.Backend.Pads[0]` to `harness.Backend.Pads.Single(p => p.Slot == binding.Slot)`

- [ ] **Step 4: Run the test to verify it passes**
  Run: `dotnet test JoyNect.sln -c Release --filter "FullyQualifiedName~CombinesAJoyConPairIntoOnePadWithBothSticks"`
  Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs
git commit -m "test: fix pad index race in Joy-Con combining test"
```

---

### Task 3: Code review — reproduce and fix failing SwitchControllerSession test

**Files:**
- Modify: `tests/JoyNect.Core.Tests/Devices/SwitchControllerSessionTests.cs`

**Interfaces:**
- Consumes: `SwitchControllerSession`, `SimulatedControllerTransport`, `Harness`
- Produces: a corrected `DecodesLiveReportsAndRaisesUpdates` test

- [ ] **Step 1: Run the failing test in isolation to reproduce**
  Run: `dotnet test JoyNect.sln -c Release --filter "FullyQualifiedName~DecodesLiveReportsAndRaisesUpdates"`
  Expected: FAIL at `SwitchControllerSessionTests.cs:89` (StateUpdated not raised / assertion mismatch)

- [ ] **Step 2: Root-cause**
  Determine whether it's a timing issue (report not yet processed when `Latest` is read) or a real decode bug.

- [ ] **Step 3: Fix the test or the session code**
  - If timing: strengthen the wait condition / add a small settle delay before asserting `Latest`
  - If decode bug: fix `SwitchControllerSession.HandleReport`/`FullStateReport.TryParse`

- [ ] **Step 4: Run the test to verify it passes**
  Run: `dotnet test JoyNect.sln -c Release --filter "FullyQualifiedName~DecodesLiveReportsAndRaisesUpdates"`
  Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add tests/JoyNect.Core.Tests/Devices/SwitchControllerSessionTests.cs
git commit -m "test: fix live report decode assertion timing"
```

---

### Task 4: Code review — full source sweep for bugs and convention violations

**Files:**
- Modify: `src/JoyNect.Core/**/*.cs` as needed
- Modify: `src/JoyNect.Ui/**/*.cs` as needed
- Modify: `src/JoyNect.App/**/*.cs` as needed
- Modify: `src/JoyNect.Cli/**/*.cs` as needed

**Interfaces:**
- Consumes: all production code
- Produces: minimal bug fixes with no new public API unless required

- [ ] **Step 1: Dispatch code-reviewer agent**
  Review all source for: off-by-one errors, null dereferences, race conditions, incorrect disposal order, inconsistent state (e.g. `AppState.Mapping` returning only `Pads[0].Profile` while `Remap` writes to all pads), dead code, and convention violations.

- [ ] **Step 2: Apply each confirmed fix**
  One minimal change per bug; prefer fixing the test to pin behavior when the bug is in test logic.

- [ ] **Step 3: Run build**
  Run: `dotnet build JoyNect.sln -c Release`
  Expected: 0 warnings, 0 errors

- [ ] **Step 4: Run full test suite**
  Run: `dotnet test JoyNect.sln -c Release`
  Expected: all tests pass, 0 failures

- [ ] **Step 5: Commit**

```bash
git add src/
git commit -m "fix: source bugs from code review sweep"
```

---

### Task 5: Security audit — secrets, driver exposure, HID/registry access

**Files:**
- Modify: `src/JoyNect.Core/Output/DriverProbe.cs` as needed
- Modify: `src/JoyNect.Core/Output/ViGEmBackend.cs` as needed
- Modify: `src/JoyNect.App/Services/StartupRegistration.cs` as needed
- Modify: `src/JoyNect.App/Services/LoggingSetup.cs` as needed

**Interfaces:**
- Consumes: none new
- Produces: hardened error handling and clearer remediation messages

- [ ] **Step 1: Dispatch security-review + insecure-defaults agents**
  Audit for: hardcoded secrets (beyond `.env.release`), ViGEm driver absence handling, HID device access permissions, registry write safety, log file path exposure.

- [ ] **Step 2: Apply each confirmed fix**
  E.g. ensure `DriverProbe.ClassifyFailure` never leaks raw exception text to users; ensure registry writes are HKCU-only (already); ensure log rotation can't fill disk.

- [ ] **Step 3: Run build and tests**
  Run: `dotnet build JoyNect.sln -c Release` and `dotnet test JoyNect.sln -c Release`
  Expected: clean

- [ ] **Step 4: Commit**

```bash
git add src/
git commit -m "sec: harden driver exposure and secret handling"
```

---

### Task 6: Supply-chain audit — NuGet dependency risk

**Files:**
- Modify: `Directory.Packages.props` only if a version bump is required
- Create: none

**Interfaces:**
- Consumes: none
- Produces: none (audit only, or version pin changes)

- [ ] **Step 1: Dispatch supply-chain-risk-auditor agent**
  Audit: `HidSharp 2.6.4`, `Nefarius.ViGEm.Client 1.21.256`, `CommunityToolkit.Mvvm 8.4.2`, `Microsoft.Extensions.* 8.0.x`, `Hardcodet.NotifyIcon.Wpf 2.0.1`, `Microsoft.NET.Test.Sdk 18.11.0`, `xunit 2.9.3`. Check for known vulnerabilities, abandonment, and takeover risk.

- [ ] **Step 2: Apply version bumps only if a fix strictly requires**
  No bumps unless a confirmed vulnerability exists in a pinned version.

- [ ] **Step 3: Run build and tests**
  Run: `dotnet build JoyNect.sln -c Release` and `dotnet test JoyNect.sln -c Release`
  Expected: clean

- [ ] **Step 4: Commit (only if changes made)**

```bash
git add Directory.Packages.props
git commit -m "deps: update vulnerable NuGet packages"
```

---

### Task 7: Static analysis — Semgrep C# scan

**Files:**
- Create: none (or `.semgrep/` rules if custom rules are needed)
- Modify: source only if Semgrep finds real defects

**Interfaces:**
- Consumes: none
- Produces: none (audit only)

- [ ] **Step 1: Run Semgrep with C# ruleset**
  Run: `semgrep --config p/csharp --config p/security --error` (or equivalent available ruleset)
  Expected: no high-confidence findings; any findings triaged

- [ ] **Step 2: Triage findings**
  Real defects → fix in source. False positives → document and skip.

- [ ] **Step 3: Run build and tests after any fixes**
  Expected: clean

- [ ] **Step 4: Commit (only if fixes made)**

```bash
git add src/
git commit -m "fix: static analysis findings"
```

---

### Task 8: Final gate and consolidation

**Files:**
- Modify: none (verification only)

**Interfaces:**
- Consumes: none
- Produces: final clean state

- [ ] **Step 1: Run full build**
  Run: `dotnet build JoyNect.sln -c Release`
  Expected: 0 warnings, 0 errors

- [ ] **Step 2: Run full test suite**
  Run: `dotnet test JoyNect.sln -c Release`
  Expected: all tests pass, 0 failures

- [ ] **Step 3: Verify `.env.release` is not tracked and contains no secret**
  Run: `git status --short` and `grep -i token .env.release`
  Expected: `.env.release` shows as modified/untracked safely; no PAT present

- [ ] **Step 4: Final commit of any remaining fixes**

```bash
git add -A . ':(exclude).env.release'
git commit -m "chore: final bug-hunt fixes and gate verification"
```

---

## Self-review against spec

1. **Spec coverage:** Every goal (find bugs, fix security, reproduce failing tests, re-run gate, remove PAT) maps to a task. No gaps.
2. **Step scan:** Each step lets the implementer write exactly one reasonable thing (test, fix, or run a command). No "TBD" lines.
3. **Type consistency:** All file paths and signatures reference existing code; no new public API invented.
4. **Review Focus:** All five items are covered — Tasks 2, 3, 4, 5, and 1 respectively.
5. **Proportion:** Plan is shorter than the code it describes; bodies are signatures and assertions, not transcripts.

---

**Execution method:** You said "do as you wish" and "you can multitask and use many ai models or agents." I recommend **subagent-driven** because the five audit tasks are independent (no shared state), so each can be dispatched in parallel, then a fresh reviewer checks the whole branch at the end. This is the fastest path to "find every single bug and fix it."

Plan saved to `docs/superpowers/plans/2026-10-29-joynect-bug-hunt.md`. Proceeding with subagent-driven development — dispatching all five audit agents in parallel now.