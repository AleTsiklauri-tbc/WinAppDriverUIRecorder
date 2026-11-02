# AGENTS.md

Instructions for AI coding assistants working in this repository.

**WinForms Recorder** is a .NET 10 WPF locator inspector and test recorder for .NET Framework WinForms apps automated
with WinAppDriver and Java. It replaces Microsoft's WinAppDriver UI Recorder, kept unmodified in `legacy/UIRecorder`.
The user is a QA automation engineer at a bank; the real target app is LMS, a loan management system.

**Status: Phases 0–2 of the spec are done; Phase 3 (recording) is next. Read [docs/HANDOFF.md](docs/HANDOFF.md) before
anything else.**

## Sources of truth

- [docs/SPEC.txt](docs/SPEC.txt): the user's spec, verbatim. Phases are in §14, engineering rules in §17. Its opening
  says to start at Phase 0: don't, Phases 0–2 are done.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): decisions (D-n) and verified findings (F-n). Extend them; never
  contradict them silently.
- [docs/target-style/](docs/target-style/): the Java that generated code must match, and the filled-in spec §15 profile.

## Working with the user

- One phase at a time. When a phase is done, reply with exactly the §14 report (Built, Files, Verify it yourself, Tests,
  Decisions & open questions) and stop until the user says "continue".
- Ask before decisions that are expensive to reverse, and list every deviation from the spec in the report.

## Rules

- Don't guess UI Automation or WinAppDriver behaviour. Write a spike test against `tests/Recorder.SampleApp`, observe,
  implement, then record the fact as F-n in ARCHITECTURE.md §7. Record design choices as D-n in §5.
- No TODOs, stubs or "simplified for now" in a phase reported as done.
- Classes ≤ ~300 lines. No logic in code-behind beyond view concerns. Async all the way; never `.Result` or `.Wait()` on
  the UI thread. The UI thread never calls UI Automation; every UIA call path takes a CancellationToken and has a timeout.
- `src/` builds with nullable reference types, the .NET analyzers and `TreatWarningsAsErrors`: zero warnings. Frequent
  ones: CA1720 (type names such as `Guid` or `Single` in identifiers), CA1716 (keywords such as `Handles`), CA1822,
  CA1826, CA1873 (no method calls inside logging arguments).
- All user-facing text lives in `src/Recorder.App/Resources/Strings.resx`.
- Network code only in `src/Recorder.Core/Verification`. `NoNetworkingTests` fails if `HttpClient`, `WebRequest`,
  `WebClient`, `System.Net.Sockets`, `Telemetry` or `ApplicationInsights` appears anywhere else under `src/`, comments
  included. No telemetry, crash-reporting or updater packages.
- Privacy: never read the value of an `IsPassword` element, never record printable keystrokes, never log element names,
  values or window titles at Information level, and never derive names from LMS window titles: they contain customer
  names and client numbers.
- Never invoke UIA patterns on the target app (F-14). Tests click the SampleApp with `Click.Post`.
- Don't modify `legacy/`.
- Files are UTF-8 without BOM, with CRLF line endings (`.editorconfig`).
- Tests that move the real mouse or send keys carry `[Trait("Category", "Input")]`. Tests that need WinAppDriver carry
  `[Trait("Requires", "WinAppDriver")]` and skip when it isn't reachable.
- A failing UI test is evidence. Find the cause before changing it (check the environment first: a locked session, a
  window someone closed), and never loosen a test just to make it pass.

## Commands

```powershell
./build.ps1                    # build; src/ treats warnings as errors
./build.ps1 test -NoInput      # all tests except the mouse/keyboard ones (about a minute after the build)
./build.ps1 test               # all tests; the mouse/keyboard ones run first, hands off for about a minute
./build.ps1 test -WinAppDriverUrl http://127.0.0.1:4723   # also the WinAppDriver comparisons (about 11 more minutes)
./build.ps1 run                # start the recorder
```

- `build.ps1` finds the .NET 10 SDK even when it is installed per user and not on PATH, and sets `DOTNET_ROOT` so the
  recorder can start. Call `dotnet` directly only with that same SDK (`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` for a
  per-user install).
- Restore failing with `NU1301` for `hdprepo.hq.tbc` means this PC is off the bank's network: see
  [docs/HANDOFF.md](docs/HANDOFF.md), "Setting up another PC".
