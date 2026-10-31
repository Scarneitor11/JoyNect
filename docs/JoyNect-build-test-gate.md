# JoyNect build/test gate — campaign Step 2

Date: 2026-10-29
Tree: current checkout, post LightsPage edit
Configuration: `dotnet build JoyNect.sln -c Release`, then `dotnet test JoyNect.sln -c Release --no-build`

## 1. Build verdict

Verdict: **compiles cleanly**.

- `dotnet build JoyNect.sln -c Release`
- Exit code: 0
- Output summary:
  - JoyNect.Core -> `src/JoyNect.Core/bin/Release/net8.0-windows/JoyNect.Core.dll`
  - JoyNect.Ui -> `src/JoyNect.Ui/bin/Release/net8.0-windows/JoyNect.Ui.dll`
  - JoyNect.Cli -> `src/JoyNect.Cli/bin/Release/net8.0-windows/JoyNect.Cli.dll`
  - JoyNect.App -> `src/JoyNect.App/bin/Release/net8.0-windows10.0.19041.0/JoyNect.dll`
  - JoyNect.Core.Tests -> `tests/JoyNect.Core.Tests/bin/Release/net8.0-windows/JoyNect.Core.Tests.dll`
- Warnings: 0
- Errors: 0
- Total time: 00:00:01.71

Notes:
- The build included the modified `src/JoyNect.App/Pages/LightsPage.cs` and produced no new compile errors.
- No warning suppression or hand-waving was used; the tail of the build output shows 0 warnings, 0 errors.

## 2. Test verdict

Verdict: **not pass**. One test failed.

- `dotnet test JoyNect.sln -c Release --no-build`
- Exit code: 0 (VSTest runner still exited 0, but 1 test failed — exit code alone is not the pass signal here)
- Results: Passed 174, Failed 1, Skipped 0, Total 175
- Duration: 14 s

Failing test:
- JoyNect.Core.Tests.Devices.ControllerManagerTests.CombinesAJoyConPairIntoOnePadWithBothSticks

Failure details:
- Exception: System.ArgumentOutOfRangeException
- Message: Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')
- Stack trace top:
  - `System.SZArrayHelper.get_Item[T](Int32 index)`
  - `JoyNect.Core.Tests.Devices.ControllerManagerTests.CombinesAJoyConPairIntoOnePadWithBothSticks()` at `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs:line 72`

Classification:
- This is one of the end-to-end `ControllerManagerTests` cases that uses the test harness with a recording backend and simulated controllers.
- Likely candidate class: a timing-dependent assertion that indexes into an array/collection too early, probably related to waiting for the combined Joy-Con pad to be created and then indexing `harness.Backend.Pads[0]` or a similar collection before it is populated.
- This looks like a test-harness/Timing issue rather than a fundamental mapping/composition logic bug — the failure is an out-of-range index during setup/wait, not a clearly wrong composed stick value.
- I am not finalizing the root cause here; that belongs to the next campaign step if/when we decide to fix it. This step is record-only.

## 3. Gate decision

- Build: passes.
- Tests: does not pass cleanly. One failure.
- Gate status: **entered, but with a failing test recorded**. Per the campaign plan Step 2, this failure is now written evidence. Any fix is part of the campaign and will be classified later as required/nice-to-have/env-blocked; no fix is landed in this step.

## 4. Context / what changed since the earlier gate run this session

- The earlier gate run (context only, not the authority) showed all tests passing.
- Since then, only `src/JoyNect.App/Pages/LightsPage.cs` was edited in this campaign.
- The failing test is in `tests/JoyNect.Core.Tests/Devices/ControllerManagerTests.cs`, which is not the file that was edited. That does not rule out an unrelated flaky test, but it does mean the failing test is not a direct compile-time consequence of the LightsPage edit; diagnosis is still required before any fix claim.

## 5. Plain-text evidence captured for the record

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

## 6. Next step gate

Before any code change motivated by this gate:
1. Root-cause the single failing test (Phase 1 systematic-debugging: reproduce, trace, do not guess).
2. Decide whether it is a real behavior bug or a flaky/timing-sensitive harness assertion.
3. If real, land the smallest fix and re-run the gate.
4. If flaky/harness-only, record that clearly and decide campaign treatment.

This artifact is the campaign's written Step 2 record. It does not authorise any fix yet.
