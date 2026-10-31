# JoyNect Build/Test Gate — SwitchControllerSessionTests Fix Pass (record)

Scope: one test, `JoyNect.Core.Tests.Devices.SwitchControllerSessionTests.DecodesLiveReportsAndRaisesUpdates`.

Diagnosis recap
- Earlier gate failure: `Assert.True() Failure / Actual: False` at `SwitchControllerSessionTests.cs:89`
- Assert there: `Volatile.Read(ref updates) > 0`
- `updates` is incremented inside the `Session.StateUpdated` handler the test subscribes to
- The test already waits with `WaitFor(() => harness.Session.Latest.Buttons.Has(SwitchButtons.A), 3000)` before the assertions on `Latest`
- The test does not itself wait on `updates > 0`

Diagnosis
- The failure is best read as a flaky/liveness assert: the final assert can be reached before `StateUpdated` has been raised enough for the counter to be positive, even though the `Latest` asserts already passed
- The update path reads as intact: after Ready, `SimulatedControllerTransport` keeps emitting periodic full-state reports; `HandleFullState` updates `_latest`, sets `_hasState`, and raises `StateUpdated`
- The test does drive `Buttons` and `LeftStick` through the transport, so there is no evidenced missing-input setup problem here
- No deterministic behavior bug is evidenced from the current repro data: the test did not reproduce on rerun, on 23 targeted reruns, or on the current full gate

What changed
- `tests/JoyNect.Core.Tests/Devices/SwitchControllerSessionTests.cs`
- In `DecodesLiveReportsAndRaisesUpdates`:
  - changed the final liveness assert from
    `Assert.True(Volatile.Read(ref updates) > 0);`
    to a bounded readiness assertion on the same `StateUpdated` event the test already uses:
    `Assert.True(WaitFor(() => updates > 0, 3000), "StateUpdated was not raised after the input change");`

Why that removes the flaky assert
- The old assert could fail when the handler had not yet incremented `updates` by the time it ran, even though the event path was working
- The new assert waits on the actual signal the test cares about (`updates > 0`) with the same `WaitFor` pattern already used in the test, instead of sampling a counter at one point in time

Intent preserved
- The test still checks:
  - the session decodes live reports,
  - `Latest` reflects the expected buttons and expected left stick after the input change,
  - at least one `StateUpdated` raise occurred

Verification
- Build: `dotnet build JoyNect.sln -c Release`
  - `Compilación correcta. 0 Advertencia(s) 0 Errores`
- Single test under filter, 5 identical reruns:
  - each run: `Con error: 0, Superado: 1, Omitido: 0, Total: 1`
- Full gate: `dotnet test JoyNect.sln -c Release --no-build --nologo`
  - `Con error: 0, Superado: 175, Omitido: 0, Total: 175`

Working tree note
- This pass edits only the one named test file
- Bystander untracked files present in the working tree are not part of this fix:
  - `.env.release`
  - `JoyNect-feature-inventory.md`
  - `docs/JoyNect-build-test-gate-fix-pass.md`
  - `docs/JoyNect-build-test-gate.md`
  - `freebuff/`

New gate outcome recorded here
- Green on this tree: build clean, 175/0
- The targeted flaky assert is removed, and the test still expresses the same intended behavior
