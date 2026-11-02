# Handoff

Where WinForms Recorder stands and how to continue it on any PC. Last updated 2026-11-02, after Phase 2.

## Status

| Phase (spec §14) | State |
|---|---|
| 0. Audit and skeleton | Done |
| 1. Capture and inspector | Done, except the two-monitor check at 150 % scaling (see "Open items") |
| 2. Locator engine | Done; report sent to the user on 2026-10-29 |
| **3. Recording** | **Next. Start only when the user says "continue".** |
| 4. Code generation | Not started |
| 5. UX polish | Not started |
| 6. Packaging and docs | Not started |

- Version 0.3.0. The last full run (2026-10-29, Windows Server 2022 over remote desktop, WinAppDriver 1.2.2009) passed
  all 328 tests: 257 unit tests and 71 integration tests. Of those, 13 use the real mouse and keyboard and 4 need WinAppDriver.
- Everything needed to continue is in this repo.

## Read first

| File | Why |
|---|---|
| [AGENTS.md](../AGENTS.md) | Working rules and commands |
| [SPEC.txt](SPEC.txt) | The user's spec, verbatim: the source of truth. Phases 0–2 are done; don't redo them |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Design, decisions D-1 to D-41, verified findings F-1 to F-24 |
| [target-style/](target-style/) | Real Page, Steps and Test files from the user's Java framework, and the filled-in spec §15 profile |
| [README.md](../README.md) | How to build, run and use the recorder |

## Setting up another PC

1. Windows 10 or 11, x64. The work so far was done on Windows Server 2022. The build needs no admin rights.
2. Clone: `git clone https://github.com/AleTsiklauri-tbc/WinAppDriverUIRecorder.git`. The folder name doesn't matter.
3. .NET SDK 10.0.403 or a later 10.0 SDK (`global.json` rolls forward to newer feature bands). Check with
   `dotnet --list-sdks`; README.md shows a per-user install. The .NET Framework 4.8 Developer Pack is **not** needed:
   when its reference assemblies aren't installed, the SDK restores them for `Recorder.SampleApp` as a NuGet package.
4. NuGet: `NuGet.config` uses the bank's Nexus feed (`hdprepo.hq.tbc`), which works only on the corporate network.
   Anywhere else, run this once and open a new terminal:

   ```powershell
   [Environment]::SetEnvironmentVariable('WINFORMSRECORDER_NUGET_SOURCE', 'https://api.nuget.org/v3/index.json', 'User')
   ```

   It affects only this repo (D-41). Without it, restore fails with
   `error NU1301: Unable to load the service index for source https://hdprepo.hq.tbc/…`.
5. `./build.ps1 test -NoInput`. Expected: 257 + 55 passed and 3 skipped (the WinAppDriver comparisons). Two more skip if
   the WebView2 Runtime is missing; it is preinstalled on Windows 11.
6. `./build.ps1 test`. Keep your hands off the mouse during the first minute: 13 more tests drive the real mouse and keyboard.
   One of them needs WinAppDriver and is skipped without it.
7. Optional:
   - **WinAppDriver 1.2** (Developer Mode must be on in Windows 10/11). Start `WinAppDriver.exe` (port 4723), then run
     `./build.ps1 test -WinAppDriverUrl http://127.0.0.1:4723` (about 11 minutes). This is the ground truth for
     locator match counts; rerun it after any change to the locator engine.
   - **JDK 17+ and Maven** for Phase 4's compile check.

## Working without LMS

Nothing in the build, the tests or the next phases needs LMS.exe or the user's LMSAutomationDemo project:

- **`tests/Recorder.SampleApp`** stands in for LMS: .NET Framework 4.8 WinForms, MDI, owned dialogs and a MessageBox, a
  DataGridView with a checkbox column, image-only toolbar buttons, and an owner-drawn MSAA-only control standing in for
  DevExpress. It also has a WebView2 form and Georgian text. Every acceptance test in the spec runs against it.
- **`docs/target-style/`** holds the Java style that Phase 4 must generate.

Only these need the work PC with LMS:

- Checking how DevExpress controls (XtraEditors, XtraGrid, XtraNavBar) appear: AutomationIds, names, tree shape. This
  has never been done; F-11 describes only the stand-in.
- Trying each finished phase on real LMS screens.

Never copy anything from LMS screens into this repo, the tests or the logs: window titles and grids show customer names
and client numbers.

## Next: Phase 3 (recording)

**Spec sections.**
- §9: the recording engine.
- §6, `RecordedStep`: the step kinds.
- §5: the `Recording/` and `Persistence/` folders.
- §11.2: Steps list. §11.4: mini-bar. §11.6: undo/redo.
- §12: `*.wfrec.json` recordings.
- §14: the acceptance test.

**Acceptance (§14).** Recording this SampleApp flow produces exactly these steps: type amount → type password → select
"GEL" → tick checkbox → File › Export › PDF → open dialog → click OK on MessageBox → click grid cell (row 2, "Status").
A test greps the saved recording and the logs and confirms the password text appears nowhere.

**Ask the user before building.** These were flagged before Phase 0 and are still open:

1. "open dialog → click OK on MessageBox". In the SampleApp, **Open dialog…** opens `ConfirmDialog` (OK and Cancel),
   and **Show message** opens a MessageBox ("Information", OK). Is "open dialog" the click that opens the MessageBox, or
   should ConfirmDialog's OK open a MessageBox?
2. "click grid cell (row 2, "Status")". Rows are 0-based both in UI Automation (`Row 0`, `Row 1`…) and in the user's
   `GridSteps.clickCell` (D-12). Does row 2 mean `Row 2` (1003, Overdue) or the second row (`Row 1`, 1002, Closed)?

**Already there to build on.**

- `Recording/MouseHookService`: the `WH_MOUSE_LL` hook that Pick mode uses through `Automation/PickSession`.
- `UiaService`: `CaptureAtAsync` and `HoverAtAsync` on one worker thread, with timeouts and Busy answers.
  `HoverTracker` polls the cursor every 25 ms with a 40 ms debounce.
- `IValueReader` / `ValuePatternReader`: values are read through it, never for `IsPassword` elements. Tests check this
  with `SpyValueReader`.
- Elements and locators:
  - `ElementCatalog` and `PageElement` (variable name, locators).
  - `PageKey`: one page per nearest Form (D-8).
  - `ElementLocating` and `LocatorService` work out locators for new elements.
- The toolbar's Record, Pause, Stop, Assert, Undo, Redo and Export buttons exist but are disabled (D-16). The Steps tab
  shows its empty state.
- ARCHITECTURE.md §4, row `MouseKeyboardEventHandler.cs`, sketches the design:
  - The hook only copies the event and returns.
  - The clicked element comes from a hover cache younger than 250 ms, or is looked up again at the click point.
  - Double-clicks use the system double-click time; movement under 8 px still counts as a click.

**Traps found in Phases 1–2.**

- Never invoke UIA patterns on the target (F-14). After a click that opens a modal dialog, every later UIA call into
  that app times out. Tests click the SampleApp with `Click.Post` (PostMessage). Recording must only observe.
- A frozen target answers Busy after 2.5 s (F-16, D-29); no click may be lost meanwhile.
- Owned dialogs, MessageBoxes and menus sit under the main window, both in UIA and in WinAppDriver (F-7, F-23), so they
  need no SwitchWindow step. An open ComboBox list is an unowned pop-up (F-23, D-38).
- WebView2 elements belong to `msedgewebview2.exe`, not to the app (F-12, D-24).
- Menu items have no AutomationId, and an open menu lists them twice (F-8, F-21).
- Driving the recorder's own toolbar through UIA activates the recorder and lifts it over the SampleApp. End-to-end tests
  call `Sample.BringToFrontAt(point)` afterwards, before using real input.

## Later phases

**Phase 4, code generation.**
- Spec §15 is already filled in, in [target-style/README.md](target-style/README.md); the user confirmed it with the
  pre-build analysis. Mention it in the Phase 4 plan, and ask only if something looks out of date.
- Three layers (D-4, D-5):
  - Page: one-line getters.
  - Steps: fluent methods that `return this;` and call `log.info` for each action.
  - Test: TestNG, with ordered `@Test(priority = n)` methods.
- java-client 7.6.0 means `findElementByAccessibilityId/ByName/ByXPath/ByClassName`; `AppiumBy` doesn't exist in that
  version (D-5). Today's "Copy Java line" already uses that getter format (`JavaLocatorLine`, D-32).
- How to generate specific steps:
  - Passwords: the Steps method takes a parameter and the test passes `ConfigReader.getProperty("<KEY>")` (D-9).
  - Combos: `sendKeys(text)` then `Keys.ENTER`.
  - Grids: `GridSteps.clickCell(rowIndex, header)`, 0-based (D-12).
- Don't generate what the user's suite does today and causes trouble:
  - `@LocalizedControlType` in XPaths (depends on the Windows language).
  - Index-based lookups.
  - XPaths anchored on the main window title (it contains user and customer names).
  - Angular state classes used as ClassName.
  - `contains(@Name,'Row 1')`, which also matches rows 10–19.
  - `Thread.sleep`.
- Compile check (D-10): `javac` against java-client 7.6.0 and Selenium 3.141.59 plus stubs of `BasePage`, `BaseSteps`
  and `BaseTest`. The jars come from `~/.m2`; on a new PC, fetch them with Maven first. The check is skipped when no JDK
  is found.
- The packages the spec plans for this phase (Scriban, AvalonEdit, DiffPlex) are on nuget.org and in the Nexus group.

**Phase 5, polish.** Autosave, the Settings UI (the WinAppDriver address moves there from the Verify panel, D-37), hotkey
conflicts, accessibility, light/dark themes, microcopy, performance budgets.

**Phase 6, packaging.** A self-contained single-file `win-x64` exe, so users no longer need .NET or `build.ps1 run`.

## Open items

- The DevExpress check on LMS (work PC only).
- Phase 1's acceptance at 100 % and 150 % scaling on two monitors. It was never possible on the remote-desktop host. On
  a physical PC with two monitors: set one to 150 %, move the SampleApp to each monitor in turn, run
  `CaptureEndToEndTests`, and watch for leftover highlights.
- The two Phase 3 questions above.

## Testing notes

- `./build.ps1 test` runs the 13 mouse/keyboard tests first (about 45 s). Keep the session unlocked and any remote-desktop
  window restored, and don't touch the mouse. Otherwise Windows blocks simulated input (`SendInput was blocked (error 5)`).
- With WinAppDriver running, the comparison tests then keep a **Recorder Sample** window in front for about 10 minutes.
  Other windows are fine to use; closing or clicking that one fails the run.
- Integration tests copy the recorder's log to
  `tests/Recorder.IntegrationTests/bin/<configuration>/net10.0-windows/TestResults/recorder-logs/`.
- If a findings test fails on another Windows version, treat it as a new finding: spike, record F-n, adapt the code. Don't
  loosen the test. Several Phase 2 failures turned out to be the environment (a locked session, a test window someone
  closed), so rule that out first.
