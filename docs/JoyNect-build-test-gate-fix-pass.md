# JoyNect Build/Test Gate — Fix Pass (record)

Scope: one test, `CombinesAJoyConPairIntoOnePadWithBothSticks`, that became the campaign's gate failure.

What was changed
- `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs`
- In `CombinesAJoyConPairIntoOnePadWithBothSticks`:
  - changed the pairing readiness wait from
    `harness.Backend.Pads.Count == 1`
    to
    `manager.Pads.Count == 1`
    before indexing `manager.Pads[0]`
  - changed the later pad reference from
    `harness.Backend.Pads[0]`
    to
    `harness.Backend.Pads.Single(p => p.Slot == binding.Slot)`

Why that removes the recorded fragility
- The recorded failure was the pattern: wait on one collection (`RecordingBackend.Pads`) and then index a different collection (`ControllerManager.Pads`) at `[0]` before that second collection is definitely populated.
- The first change makes the wait precede the index on the collection that is actually indexed.
- The second change makes the later pad lookup derive from the already-reconciled binding instead of re-indexing `Backend.Pads` by position.

Test meaning is unchanged
- Still asserts: one Joy-Con pair becomes one pad, both halves feed that pad, and combined stick/button input reaches the pad.

How it was verified
- Build: `dotnet build JoyNect.sln -c Release`
  - `Compilación correcta. 0 Advertencia(s) 0 Errores`
- Target test: filtered run, 5 consecutive identical runs under `Release --no-build`
  - each run: `Con error: 0, Superado: 1, Omitido: 0, Total: 1`
- Full class: `FullyQualifiedName~ControllerManagerTests` — `Superado: 8, Con error: 0`
- Full gate: `dotnet test JoyNect.sln -c Release --no-build --nologo`
  - `Con error: 0, Superado: 175, Omitido: 0, Total: 175`

Working tree after this pass
- Modified: `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs`
- Untracked bystanders present in the working tree and not part of this fix:
  - `.env.release`
  - `JoyNect-feature-inventory.md`
  - `docs/JoyNect-build-test-gate-fix-pass.md`
  - `docs/JoyNect-build-test-gate.md`
  - `freebuff/`

New gate outcome recorded here
- The targeted fragility is removed and the gate is green on this tree: build clean, 175/0.
- The change is bounded to one test. A separate unrelated failure also exists in this tree and is not touched by this pass: `SwitchControllerSessionTests.DecodesLiveReportsAndRaisesUpdates` at `SwitchControllerSessionTests.cs:89`. That failure was observed in the same full-gate run and is recorded here only for traceability; it is outside the scope of this fix pass.

If anything regresses
- None observed during this pass. The target test passed all 5 targeted reruns and the full `ControllerManagerTests` class passed; the full gate passed at 175/0.
