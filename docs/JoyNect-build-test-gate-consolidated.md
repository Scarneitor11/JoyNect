# JoyNect Build/Test Gate — consolidated record

Date: 2026-10-29
Tree: current checkout
Configuration:
- build: `dotnet build JoyNect.sln -c Release`
- test: `dotnet test JoyNect.sln -c Release --no-build` (with `--nologo` on rerun evidence)
- runtime: `.NET 8.0.426` on this machine

Sources merged into this record:
- `docs/JoyNect-build-test-gate.md` (original Step 2 gate record)
- `docs/JoyNect-build-test-gate-fix-pass.md` (ControllerManagerTests fix pass)
- `docs/JoyNect-build-test-gate-session-fix-pass.md` (SwitchControllerSessionTests fix pass)

This is the campaign's written source of truth for the build/test gate squeeze done so far:
the original gate result, the two targeted failures, their diagnoses, the two fixes, and the
re-proven outcome. It does not invent new failures or new fixes.

---

## 1. Gate entrance result (original)

Build verdict: **compiles cleanly**.

Run:
- `dotnet build JoyNect.sln -c Release`
- Exit code: 0
- Output summary (from the original record):
  - JoyNect.Core -> `src/JoyNect.Core/bin/Release/net8.0-windows/JoyNect.Core.dll`
  - JoyNect.Ui -> `src/JoyNect.Ui/bin/Release/net8.0-windows/JoyNect.Ui.dll`
  - JoyNect.Cli -> `src/JoyNect.Cli/bin/Release/net8.0-windows/JoyNect.Cli.dll`
  - JoyNect.App -> `src/JoyNect.App/bin/Release/net8.0-windows10.0.19041.0/JoyNect.dll`
  - JoyNect.Core.Tests -> `tests/JoyNect.Core.Tests/bin/Release/net8.0-windows/JoyNect.Core.Tests.dll`
- Warnings: 0
- Errors: 0
- Total time: 00:00:01.71

Test verdict: **not pass**. One test failed.

Run:
- `dotnet test JoyNect.sln -c Release --no-build`
- Exit code: 0 from the runner, but 1 test failed; exit code alone is not the pass signal here
- Results: Passed 174, Failed 1, Skipped 0, Total 175
- Duration: 14 s

Failing test:
- `JoyNect.Core.Tests.Devices.ControllerManagerTests.CombinesAJoyConPairIntoOnePadWithBothSticks`

Failure details:
- Exception: `System.ArgumentOutOfRangeException`
- Message: `Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')`
- Stack trace top:
  - `System.SZArrayHelper.get_Item[T](Int32 index)`
  - `JoyNect.Core.Tests.Devices.ControllerManagerTests.CombinesAJoyConPairIntoOnePadWithBothSticks()` at
    `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs:line 72`

Original classification:
- The original record did not finalize root cause there; it recorded the failure shape and a likely
  class: a timing/harness-indexing problem where the test waits for one collection and then indexes a
  different collection before it is populated.
- That "likely class" was a preliminary interpretation, not an evidenced diagnosis. The evidence behind
  the later, tighter classification is in section 2.

Original gate decision:
- Build: passes.
- Tests: does not pass cleanly. One failure.
- Gate status: entered, but with a failing test recorded. Any fix would be part of the campaign and
  classified later as required/nice-to-have/env-blocked; no fix was landed in the entrance step.

Context recorded in the original record:
- The earlier gate run this session (context only, not authority) showed all tests passing.
- Since then, only `src/JoyNect.App/Pages/LightsPage.cs` had been edited in the campaign.
- The failing test file is `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs`, which is not
  the file that was edited. That does not rule out a flaky test; it only means the failure is not a
  direct compile-time consequence of the LightsPage edit, so diagnosis was still required before any
  fix claim.

Plain-text evidence captured in the original record:

Build tail:
```
Compilación correcta.
    0 Advertencia(s)
    0 Errores
Tiempo transcurrido 00:00:01.71
EXIT:0
```

Test tail:
```
Con error! - Con error:     1, Superado:   174, Omitido:     0, Total:   175, Duración: 14 s - JoyNect.Core.Tests.dll (net8.0)
EXIT:0
```

Failure identity:
```
JoyNect.Core.Tests.Devices.ControllerManagerTests.CombinesAJoyConPairIntoOnePadWithBothSticks [FAIL]
System.ArgumentOutOfRangeException : Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')
...ControllerManagerTests.cs:line 72
```

---

## 2. Targeted failures

### Failure A — ControllerManagerTests.CombinesAJoyConPairIntoOnePadWithBothSticks

Failure shape:
- Same as the original gate record: `ArgumentOutOfRangeException` with an out-of-range index during
  test execution, recorded at `ControllerManagerTests.cs:line 72`.

What the committed test did before the fix:
- Waited on `harness.Backend.Pads.Count == 1` with:
  `Assert.True(WaitFor(() => harness.Backend.Pads.Count == 1), "a Joy-Con pair should become one pad")`
- Then indexed the other collection:
  `var binding = manager.Pads[0]`
- Later used:
  `var pad = harness.Backend.Pads[0]`

The two collections are different and are populated by different code paths:
- `harness.Backend.Pads` is the test `RecordingBackend.Pads`, populated only inside
  `RecordingBackend.CreatePad` under a lock when the manager creates a pad.
- `manager.Pads` is `ControllerManager.Pads`, populated only after a pad binding exists and is inserted
  under `_padGate`/`_gate` in `ControllerManager.EnsureBinding`.

Tightened diagnosis:
- The concrete fragility is the same class recorded in the original pass: the test waits for one
  collection to reach 1 and then indexes a different collection at `[0]` whether or not that second
  collection has been populated yet.
- The exact indexed collections are:
  - waited on: `RecordingBackend.Pads` via `harness.Backend.Pads.Count`
  - indexed too early: `ControllerManager.Pads` via `manager.Pads[0]`
- The later `harness.Backend.Pads[0]` usage sits after the same `Backend.Pads.Count == 1` wait and is
  therefore guarded for that specific access, but the main failure path is the `manager.Pads[0]` line.

Repro evidence:
- After restoring the working tree to the committed form and rebuilding, one targeted rerun of the single
  test passed, 5 consecutive identical reruns passed, the full `ControllerManagerTests` class passed, and
  the full gate passed.
- On that committed tree the failure was not reproducible, which is why the open diagnosis was narrowed to
  "fragile/timing-dependent harness assertion" rather than "deterministic production bug."
- This is evidence for a fragility classification, but it does not disprove that a real underlying timing
  gap existed; it only means the specific tree/build/test run did not reproduce it.

Classification:
- Fragile harness synchronization, not evidenced as a deterministic production mapping/composition bug.
- Evidence: failure is an out-of-range index during setup/assert, not a wrong composed stick value; the
  fragility has a concrete two-collection form; same test and same class were green on the identical
  committed tree and binaries.

---

### Failure B — SwitchControllerSessionTests.DecodesLiveReportsAndRaisesUpdates

Failure shape:
- `Assert.True() Failure`
- `Actual: False`
- Failing assert at `SwitchControllerSessionTests.cs:89`

What the test asserted:
- After the session reached Ready and the test wrote
  `harness.Transport.Buttons = SwitchButtons.A | SwitchButtons.Zr | SwitchButtons.LeftStickClick` and
  `harness.Transport.LeftStick = new StickRaw(1200, 3000)`, the test asserted:
  - `WaitFor(() => harness.Session.Latest.Buttons.Has(SwitchButtons.A), 3000)`
  - the expected button set on `Latest.Buttons`
  - the expected left stick on `Latest.LeftStick`
  - `Volatile.Read(ref updates) > 0`

Where `updates` comes from:
- The test subscribes to `Session.StateUpdated`:
  `harness.Session.StateUpdated += _ => Interlocked.Increment(ref updates);`
- `updates` is incremented inside that handler.

Root-cause reading:
- The final assert is a liveness/counter sample: `Volatile.Read(ref updates) > 0`.
- The test already waits on `Latest.Buttons.Has(A)` with a 3000ms timeout, but it does not itself wait on
  `updates > 0`.
- So the final assert can be reached before `StateUpdated` has been raised enough for the counter to be
  positive, even though the `Latest` asserts already passed.

Update path read as intact:
- After Ready, `SimulatedControllerTransport` keeps emitting periodic full-state reports.
- The session loop decodes them in `HandleFullState`, which sets `_latest`, sets `_hasState`, and raises
  `StateUpdated`.
- The test drives `Buttons` and `LeftStick` through the transport, so there is no evidenced missing-input
  test-setup fidelity problem in the usual sense.

Repro evidence:
- The failure did not reproduce on rerun, on 23 targeted filtered reruns, or on the current full gate.
- That is evidence that the failure is not a deterministic behavior regression that is easy to hit on this
  tree/build, not proof that it can never happen.

Classification:
- Flaky/liveness assert: a one-shot `updates > 0` sample that can be reached before the handler has fired
  enough times in that run.
- Not evidenced as a deterministic behavior regression from the current repro data.
- Evidence: same assert repeatedly passes; failure shape is a plain false assert with no exception; the
  update path involved is intact and exercised by the session loop.

---

## 3. Targeted fixes

### Fix A — ControllerManagerTests.cs

File: `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs`

What changed:
- In `CombinesAJoyConPairIntoOnePadWithBothSticks`:
  - the pairing-waits-for-one-pad assertion now waits on
    `manager.Pads.Count == 1`
    instead of
    `harness.Backend.Pads.Count == 1`
  - the later pad reference changed from
    `var pad = harness.Backend.Pads[0]`
    to
    `var pad = harness.Backend.Pads.Single(p => p.Slot == binding.Slot)`

Why that removes that fragility class:
- The first change makes the wait precede the index on the collection that is actually indexed: the `[0]`
  access on `manager.Pads` is now after the test has confirmed `manager.Pads` contains the combined pad.
- The second change replaces positional indexing into `harness.Backend.Pads` with a lookup derived from the
  already-reconciled binding, so the later pad reference no longer depends on a separate positional
  wait-and-index pattern.

Scope:
- Only this test was touched.

Test meaning preserved:
- The test still asserts:
  - one Joy-Con pair becomes one pad,
  - both halves feed that pad,
  - combined stick/button input reaches the pad.

---

### Fix B — SwitchControllerSessionTests.cs

File: `tests/JoyNect.Core.Tests/Devices/SwitchControllerSessionTests.cs`

What changed:
- In `DecodesLiveReportsAndRaisesUpdates`:
  - the final liveness assert changed from
    `Assert.True(Volatile.Read(ref updates) > 0);`
    to
    `Assert.True(WaitFor(() => updates > 0, 3000), "StateUpdated was not raised after the input change");`

Why that removes the flaky assert:
- The old assert sampled the handler counter at one point in time and could fail even when the update path
  was working, if the handler had not yet incremented `updates` by the time the assert ran.
- The new assert waits on the actual signal the test cares about (`updates > 0`) using the same `WaitFor`
  pattern already used in the test, instead of one-shot timing sampling.

Scope:
- Only this test was touched.

Test meaning preserved:
- The test still checks:
  - the session decodes live reports,
  - `Latest` reflects the expected buttons and expected left stick after the input change,
  - at least one `StateUpdated` raise occurred.

---

## 4. Re-proven outcome

Build (after both fixes):
- `dotnet build JoyNect.sln -c Release`
- `Compilación correcta. 0 Advertencia(s) 0 Errores`
- Total time: 00:00:01.62

Test (after both fixes):
- `dotnet test JoyNect.sln -c Release --no-build --nologo`
- `Con error: 0, Superado: 175, Omitido: 0, Total: 175`
- Duration: 14 s

Single-test rerun evidence:

Failure A (ControllerManagerTests.CombinesAJoyConPairIntoOnePadWithBothSticks):
- Filtered test, 5 consecutive identical reruns after the fix.
- Each run: `Con error: 0, Superado: 1, Omitido: 0, Total: 1`
- Full `ControllerManagerTests` class after the fix: `Superado: 8, Con error: 0`

Failure B (SwitchControllerSessionTests.DecodesLiveReportsAndRaisesUpdates):
- Filtered test, 5 consecutive identical reruns after the fix.
- Each run: `Con error: 0, Superado: 1, Omitido: 0, Total: 1`
- The earlier non-fix diagnosis run had also passed 23 targeted reruns plus the current full gate; the
  fix pass recorded here re-proves the same test under the same filter with the same green shape.

Committed state as of this record

- The two test files modified by the campaign's two targeted fixes are committed in `42331fd`.
- Current working tree on this checkout: clean; no modified files beyond the untracked bystanders listed below.
- Committed tip: `42331fd` (delivery commit for the two bounded test-only fixes).
- Untracked bystanders present, not part of these fixes:
  - `.env.release`
  - `JoyNect-feature-inventory.md`
  - `docs/JoyNect-build-test-gate.md`
  - `docs/JoyNect-build-test-gate-fix-pass.md`
  - `docs/JoyNect-build-test-gate-session-fix-pass.md`
  - `docs/JoyNect-build-test-gate-consolidated.md`
  - `docs/JoyNect-build-test-gate-hygiene.md`
  - `docs/superpowers/`
  - `freebuff/`

---

## 5. Honest residual note

Campaign rule: no test can be proven never to flake.

After these passes:
- Failure A still has residual uncertainty, but the specific fragility that was recorded is removed: the
  remaining test no longer waits on one collection and then indexes a different collection at `[0]`.
- Failure B still has residual uncertainty, but the specific flaky one-shot sampling assert is removed: the
  remaining test now waits on the actual `StateUpdated`-driven signal it cares about.
- So the residual risk for each fixed test is now narrower than before: a future failure of either test
  would be more likely to point to something real changing in the relevant path, rather than to the exact
  same harness fragility already recorded and removed here.

This record does not claim either test is proved perfectly stable forever. It claims only what was done and
what was observed this session.

---

## 6. Is the campaign's gate record now coherent?

Yes, for the two failures chased in this squeeze:
- There is one original gate record.
- There is one tightened diagnosis + fix + verification record for Failure A.
- There is one diagnosis + fix + verification record for Failure B.
- There is now one consolidated artifact above.

Still present but out of scope of this gate squeeze:
- Untracked bystander files exist in the working tree. They are not part of the gate verdict and are not
  claimed to be part of these fixes.
- The campaign may still want a separate decision about whether/how to commit the two test fixes and whether
  to keep or clean up the untracked bystanders.

Nothing required by this consolidation pass is missing from the record as written here.
