# JoyNect Bug Hunt — Design Doc

Date: 2026-10-29
Repo: local checkout at C:\Projects\JoyNect (mirrors https://github.com/Scarneitor11/JoyNect)
Approach selected: A — systematic parallel agent sweep

---

## Intent and why

The user wants every single bug in JoyNect found and fixed. Everything that doesn't work should be fixed. JoyNect is a .NET 8 WPF app that connects Nintendo Switch controllers to Windows and presents them as Xbox 360 pads via ViGEmBus. It has four projects (Core, Ui, App, Cli) plus 175 unit tests. The local tree is two commits behind GitHub HEAD on thread-safety fixes, and contains a hardcoded GitHub PAT in `.env.release`.

## Goals

1. Find every bug, logic error, and convention violation across all source projects.
2. Find every security issue (hardcoded secrets, insecure defaults, permissive access).
3. Reproduce and root-cause the failing tests (ControllerManager timing test, SwitchControllerSession report-decoding test).
4. Fix every confirmed bug, with the smallest possible change.
5. Re-run the build/test gate: clean build, all tests passing.
6. Remove the hardcoded token from `.env.release` and add `.env.release` to `.gitignore`.

## Non-goals

- Adding new features (mapping profiles persistence, telemetry upload, etc.) unless a bug fix requires it.
- Refactoring code that works correctly, even if it could be cleaner.
- Verifying against real Switch hardware (no hardware available; all verification is via simulation + unit tests).
- Publishing a release or changing the GitHub remote.

## Anti-goals (failure even if technically works)

- "Fixing" something that isn't broken and introducing a regression.
- Committing the GitHub PAT or any other secret to the repo.
- Declaring the gate green without actually running `dotnet build` and `dotnet test`.

## Constraints

- Windows-only; WPF, HID stack, ViGEmBus. Tests run headless against simulated controllers.
- `TreatWarningsAsErrors=true` — any new warning fails the build.
- No FluentAssertions (v8 relicensed to paid); xunit only.
- The `.env.release` secret must never be committed or exposed.
- No new NuGet packages unless a fix strictly requires one.

## Approaches considered

1. **Parallel agent sweep (selected)** — dispatch several subagents simultaneously, each with a narrow mandate (code review, security, test reproduction, dependency audit, static analysis). Consolidate findings, fix everything, re-run gate. Trades coordination overhead for speed and breadth.
2. Sequential deep dive — one person file-by-file. Slower, higher risk of missing cross-cutting issues. Dropped.
3. Targeted fixes only — fix known failures and the token, skip exploration. Fastest but misses unknown bugs. Dropped.

## The plan (as decided)

Five parallel subagents:

| Agent | Mandate | Tools |
|-------|---------|-------|
| A1 code-reviewer | Bugs, logic errors, convention violations across Core/Ui/App/Cli | read, grep, glob |
| A2 security-review + insecure-defaults | Hardcoded secrets, ViGEm exposure, HID/registry access, env handling | read, grep |
| A3 systematic-debugging | Reproduce + root-cause failing tests (ControllerManager, SwitchControllerSession) | read, shell (dotnet test) |
| A4 supply-chain-risk-auditor | NuGet dependency health and takeover risk | read, grep (packages) |
| A5 semgrep | Static analysis for common C# bug patterns | semgrep |

After agents report:
- Consolidate all findings into a single bug list.
- Fix each bug with minimal changes.
- Run `dotnet build JoyNect.sln -c Release` (warnings=errors).
- Run `dotnet test JoyNect.sln -c Release` — require 0 failures.
- Commit fixes (excluding `.env.release` secret).

## What's left to the builder (this doc)

- Whether to fix every finding or defer low-severity ones — the builder should treat confirmed bugs as fixes to land, style/convention notes as optional.
- Whether to add `.env.release` to `.gitignore` (recommended: yes).
- Whether to also fix the two commits' thread-safety changes that are on GitHub but not local (recommended: yes, they're direct fixes to local code).
- The exact wording of any commit messages.

## Builder check questions (sorted)

**Answered already (by the conversation or project):**
- Platform/language: .NET 8, C#, WPF, Windows-only. No decision needed.
- Test stack: xunit, no FluentAssertions. No decision needed.

**Minor (settle myself, mark as my call):**
- Cosmetic issues like the missing blank line in M3Slider.cs — I'll fix as part of the sweep since warnings-as-errors keeps the tree clean.
- Where findings live: I'll keep a consolidated bug list in memory and report it in one message before fixing.

**Theirs (only the human can answer, answer changes what gets built):**
- Should I fix all confirmed bugs, or only high/medium severity and defer low-severity ones? (Default: fix all confirmed bugs.)
- Should I add `.env.release` to `.gitignore`? (Default: yes, strongly recommended given the PAT.)
- Should I apply the GitHub-ahead thread-safety commits (defensive locking in ControllerManager, volatile in SwitchControllerSession)? (Default: yes.)

---

End of design doc. Please review and let me know if anything is wrong or missing before I proceed to the builder check and then the fix work.