# Handoff

Where WinForms Recorder stands and how to continue it on any PC. Last updated 2026-11-03, after Phase 6 and the checks on the work PC.

## Status

| Phase (spec §14) | State |
|---|---|
| 0. Audit and skeleton | Done |
| 1. Capture and inspector | Done, except the two-monitor check at 150 % scaling (see "Open items") |
| 2. Locator engine | Done; report sent to the user on 2026-10-29 |
| 3. Recording | Done; report sent to the user on 2026-11-02 |
| 4. Code generation | Done; report sent to the user on 2026-11-02 |
| 5. UX polish | Done; report sent to the user on 2026-11-03 |
| 6. Packaging and docs | Done; report sent to the user on 2026-11-03 |

- Version 0.7.1. There are 601 tests: 507 unit tests (2 of them compile the generated Java with javac) and
  94 integration tests, of which 17 use the real mouse and keyboard.
- The last runs were all on 2026-11-03, on the Phase 4 PC (Windows 11 Home, JDK 17). The 3 WinAppDriver comparisons were
  skipped by pointing them at an unused port, because the WinAppDriver running on that PC can't attach to windows (see
  "Open items").
  - Phase 5, one 1920×1080 monitor at 100 %: `./build.ps1 test -NoInput` passed (504 + 70), and of the 17 input tests 16
    passed and the WinAppDriver one was skipped. The light and dark screenshots of the main window, Settings and the
    cheat sheet were checked by eye.
  - With a second monitor, both at 100 % (3440×1440, and 1920×1080 to its right): the 4 `WindowPlacementTests` passed, and
    the 17 input tests with the SampleApp on the second monitor (`-Monitor 2`): 16 passed, the WinAppDriver one skipped.
    The 150 % runs were skipped (see "Open items").
  - Phase 6, against the published exe: `./build.ps1 test -NoInput -Published` passed (505 + 74, the 3 comparisons skipped),
    and of the 17 input tests 16 passed and the WinAppDriver one was skipped (77 elements hovered, all outlined correctly).
- **On the work PC** (Windows Server 2022 over remote desktop, 2026-11-03), version 0.7.1:
  - The framework classes were checked against LMSAutomationDemo (D-65). All were as assumed except `GridSteps`: other
    package, no `clickCell(int, int)`, and it finds only text cells of the grid named `Grid`. Fixed in 0.7.1 (D-83).
  - Two merge tests assumed files checked out with LF line endings; they now set their inputs' line endings.
  - 507 unit tests passed, the javac ones with JDK 21 and the jars in `~/.m2`.
  - WinAppDriver 1.2.2009, started normally on 127.0.0.1:4791: the three comparisons passed, so every generated
    candidate's count matches WinAppDriver's, including D-42's menu items and toolbar buttons. In the full run the tree
    comparison failed once with "window has been closed" and passed alone.
  - Integration tests without input: 72 passed, 4 skipped (`WindowPlacementTests`, one monitor).
  - `./build.ps1 publish` worked from the Nexus feed in 63 s. The exe answered after 2.0 s, unpacked to `%TEMP%`, and ran unsigned.
  - The remote-desktop session was at 1920×1080 and 150 % during the mouse/keyboard tests: 15 of 17 passed. The other
    two fail at 150 % (see "Open items").
- Phase 6 added `./build.ps1 publish`, one self-contained 77 MB exe that starts in about 2.3 s (D-82, F-34), the README's
  quick start with three screenshots, docs/USER_GUIDE.md, and .NET itself in THIRD_PARTY_NOTICES.md.
- Phase 5 added the Settings window, shortcut conflict handling and the cheat sheet, autosave and restore, recent files,
  window and pane memory, the theme choice, toasts under the mini-bar, an accessibility and contrast pass, and timings
  (ARCHITECTURE D-71 to D-81, F-31 to F-33).
- After the Phase 4 report the user asked to fix, before Phase 5, the window titles found in saved recordings: done (D-70).
- The user's answers to the two questions left open before Phase 3: "row 2" counts from 0 (`Row 2`, 1003, Overdue), and
  "open dialog → click OK on MessageBox" is the click on **Show message** followed by OK, which only closes the box.
- After the Phase 3 report the user decided that Enter and Tab in a password field are recorded (D-59, a deviation from spec 9.4).
- Everything needed to continue is in this repo.

## Read first

| File | Why |
|---|---|
| [AGENTS.md](../AGENTS.md) | Working rules and commands |
| [SPEC.txt](SPEC.txt) | The user's spec, verbatim: the source of truth. All six phases are done; don't redo them |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Design, decisions D-1 to D-83, verified findings F-1 to F-34 |
| [TEMPLATES.md](TEMPLATES.md) | Code profiles and the template model |
| [target-style/](target-style/) | Real Page, Steps and Test files from the user's Java framework, and the filled-in spec §15 profile |
| [README.md](../README.md) | What the recorder is, the quick start, and how to build, test and publish it |
| [USER_GUIDE.md](USER_GUIDE.md) | Using the recorder, on one page |

## Setting up another PC

1. Windows 10 or 11, x64. Phases 0–2 were built on Windows Server 2022, Phase 3 on Windows 11 Home. The build needs no admin rights.
2. Clone: `git clone https://github.com/AleTsiklauri-tbc/WinAppDriverUIRecorder.git`. The folder name doesn't matter.
3. .NET SDK 10.0.403 or a later 10.0 SDK (`global.json` rolls forward to newer feature bands only, so 10.0.3xx is not enough). Check
   with `dotnet --list-sdks`; README.md shows a per-user install. The .NET Framework 4.8 Developer Pack is **not** needed: when its
   reference assemblies aren't installed, the SDK restores them for `Recorder.SampleApp` as a NuGet package.
4. NuGet: `NuGet.config` uses the bank's Nexus feed (`hdprepo.hq.tbc`), which works only on the corporate network.
   Anywhere else, run this once and open a new terminal:

   ```powershell
   [Environment]::SetEnvironmentVariable('WINFORMSRECORDER_NUGET_SOURCE', 'https://api.nuget.org/v3/index.json', 'User')
   ```

   It affects only this repo (D-41). Without it, restore fails with
   `error NU1301: Unable to load the service index for source https://hdprepo.hq.tbc/…`.
5. `./build.ps1 test -NoInput`. Expected: the 507 unit tests pass, and 70 integration tests pass with 7 skipped: the 3
   WinAppDriver comparisons, and the 4 `WindowPlacementTests`, which need two monitors (with two: 74 passed, 3 skipped).
   Two more skip if the WebView2 Runtime is missing (it is preinstalled on Windows 11), and the two javac tests skip
   without a JDK or the jars.
   The unit tests run first, then the integration tests: both time things against the spec's budgets (F-31).
6. `./build.ps1 test`. Keep your hands off the mouse and keyboard during the first minute and a half: 17 more tests drive the real
   mouse and keyboard (one types into the SampleApp and uses the Save dialog). One of them needs WinAppDriver and is skipped without it.
7. Optional:
   - **WinAppDriver 1.2** (Developer Mode must be on in Windows 10/11). Start `WinAppDriver.exe` (port 4723), then run
     `./build.ps1 test -WinAppDriverUrl http://127.0.0.1:4723` (about 11 minutes). This is the ground truth for
     locator match counts; rerun it after any change to the locator engine or to capture.
   - **A JDK 17+ and six jars** for the Java compile check: see [tests/JavaCompileCheck/README.md](../tests/JavaCompileCheck/README.md).
     On the Phase 4 PC: Microsoft's OpenJDK 17 unpacked per user to `%LOCALAPPDATA%\Microsoft\jdk-17`, and the jars from Maven
     Central in `tests/JavaCompileCheck/lib` (ignored by git). On the work PC, a build of LMSAutomationDemo leaves them in `~/.m2`.
8. `./build.ps1 publish` makes the exe in about 2.5 minutes; the first time, it downloads the .NET runtime packs (86 MB)
   from the NuGet feed. `./build.ps1 test -Published` runs the integration tests against it.

## Working without LMS

Nothing in the build or the tests needs LMS.exe or the user's LMSAutomationDemo project:

- **`tests/Recorder.SampleApp`** stands in for LMS: .NET Framework 4.8 WinForms, MDI, owned dialogs and a MessageBox, a
  DataGridView with a checkbox column and a context menu, image-only toolbar buttons, and an owner-drawn MSAA-only control
  standing in for DevExpress. It also has a WebView2 form and Georgian text. Every acceptance test in the spec runs against it.
- **`docs/target-style/`** holds the Java style the `java-appium-pom` profile generates.

Only these need the work PC with LMS:

- Checking how DevExpress controls (XtraEditors, XtraGrid, XtraNavBar) appear: AutomationIds, names, tree shape, and whether
  their editors raise focus events and expose values the way F-29 describes. This has never been done; F-11 describes only the stand-in.
- Trying each finished phase on real LMS screens.

Never copy anything from LMS screens into this repo, the tests or the logs: window titles and grids show customer names
and client numbers.

## Next

The spec's six phases are done and nothing else is planned. What is left is under "Open items", mostly checks on the work PC.

**How Phase 6 works** (ARCHITECTURE D-82, F-34): `./build.ps1 publish` publishes `src/Recorder.App` with
`Properties/PublishProfiles/win-x64.pubxml` into `artifacts/publish/win-x64/` and fails when anything but the exe lands
there, which is why `src/Directory.Build.props` embeds the PDBs. `./build.ps1 test -Published` points the integration tests
at that exe (`WINFORMSRECORDER_RECORDER_EXE`). README.md is the quick start; [USER_GUIDE.md](USER_GUIDE.md) is the one-page
guide that replaced README's "Using it" table. Keep both in step with the UI texts in `Strings.resx`. README's
screenshots (`docs/images`) show the published exe and the SampleApp in the light theme, driven through UI Automation with a
throwaway data folder that held a recording of the SampleApp flow; retake them from the SampleApp only, never from LMS.

## Open items

- **Recordings saved before D-70** (version 0.4.0 and earlier builds) can still hold a dialog's title: in its page id, a
  window's Name, or a position locator scoped by the title. Saving such a file again removes the first two, not the third.
  Record any such file again; none was made on LMS.
- **150 % scaling with a DPI-unaware target** (found on the work PC on 2026-11-03, with the remote-desktop session at
  1920×1080 and 150 %). This is the first run of Phase 1's acceptance at 150 %, and two mouse/keyboard tests fail:
  - `Hovering_any_control_…`: 33 of 77 probes fail. Two points lie off-screen, because the SampleApp is 1800×1140 at
    150 % and doesn't fit. The other 31 are all DataGridView parts (cells, headers, rows). The recorder outlines exactly
    two-thirds of their right position and size: 954,276 150×33 is drawn as 636,184 100×22. Controls with their own
    window handle are outlined correctly, and the test's own UI Automation client gets the right bounds.
  - `Recording_the_acceptance_flow_…`: "Timed out after 10 s waiting for eight steps". Which step differs isn't known yet.
    Suspect D-42 (menu and toolbar items are chosen by their bounds) and D-44 (the hover snapshot must contain the click
    point): ToolStrip items, like grid cells, are parts without a window handle.
  - Likely cause, to be proved by a spike before any fix: for parts without a window handle, served through MSAA, the
    bounds the recorder reads (cached, on its UIA worker) are in the DPI-unaware app's logical pixels. LMS is DPI-unaware
    and full of such parts (DevExpress, F-11), and many laptops run at 125–150 %, so this matters in real use.
  - Next: in a session at 150 %, compare cached and live `BoundingRectangle` of a grid cell and a menu item. Record the
    fact as F-35 and fix capture (`ElementSnapshotFactory`). Rerun `CaptureEndToEndTests` and `RecordingEndToEndTests`
    at 150 % on a screen where the SampleApp fits (2560×1440 or larger), then at 100 %.
- **Recordings saved before 0.7.1** don't know their cells' control types, so in Java all their grid cells are clicked
  inside the grid, never through `GridSteps` (D-83). Record those grid steps again to use `GridSteps`.
- **Verify (D-62).** Snapshot tests use an in-repo helper because Verify 33 ships under a maintenance-fee agreement. If the
  team sponsors Verify, it can replace `Snapshot.cs` (same file layout).
- **WinAppDriver on the Phase 3 PC.** WinAppDriver 1.2.2009 was already running there at 127.0.0.1:4723, apparently elevated,
  and the comparison tests failed with "Cannot find active window specified by capabilities: appTopLevelWindow". Run them on
  the work PC (they passed there on 2026-11-03, see "Status"), or start WinAppDriver normally first.
- The DevExpress check on LMS (work PC only).
- **The exe on colleagues' PCs.** It started on the work PC (see "Status"). Elsewhere, a policy that blocks programs in
  `%TEMP%`, where it unpacks WPF's DLLs, needs `DOTNET_BUNDLE_EXTRACT_BASE_DIR` (USER_GUIDE.md says how). It isn't
  signed, and signing needs the bank's code-signing certificate.
- **Version**: 0.7.1. When it becomes 1.0 is the user's call.
- **Uniqueness timing** (F-31): on the Phase 3–5 PC a check is almost all UI Automation's read of the window, a median of 158 ms when
  the PC is quiet, but past the 300 ms budget while it is busy (just after a build, or with other tests running). If
  `Uniqueness_checks_are_fast_enough` fails, run it alone before suspecting the code. On the work PC: median 159 ms, p95 168 ms.
- **Two monitors at different scales** (Phase 1's acceptance, D-76): not checked yet; two monitors at 100 % were checked on
  2026-11-03 (see "Status"). With one monitor at 150 %, run `./build.ps1 test -Monitor 1`, then `./build.ps1 test -Monitor 2`:
  the end-to-end tests check the outlines, the mini-bar and the assertion picker on the SampleApp's monitor, and
  `WindowPlacementTests` close and reopen the recorder on each monitor. Watch for leftover highlights, which no test sees.
  Put the 150 % on the larger monitor: the SampleApp (1200×760) doesn't fit on a 1920×1080 monitor at 150 %.
  If something fails, look first at what WPF rescales when a window moves to a monitor with another scale:
  `MiniBarWindow.Place` and `AssertPickerWindow.PlaceNextToCursor` measure their window before moving it there, and the main
  window's position is saved in device-independent pixels (D-76), whose meaning on a monitor with another scale is untested.

## Testing notes

- `./build.ps1 test` runs the 17 mouse/keyboard tests first (about 1.5 minutes). Keep the session unlocked and any remote-desktop
  window restored, and don't touch the mouse or keyboard. Otherwise Windows blocks simulated input (`SendInput was blocked (error 5)`),
  or your own typing lands in the SampleApp and fails the run.
- With WinAppDriver running, the comparison tests then keep a **Recorder Sample** window in front for about 10 minutes.
  Other windows are fine to use; closing or clicking that one fails the run.
- On the work PC a private WinAppDriver avoids the one LMSAutomationDemo uses (port 4727), and needs no admin rights:
  `& 'C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe' 127.0.0.1 4791`, then
  `./build.ps1 test -WinAppDriverUrl http://127.0.0.1:4791`. Stop it afterwards.
- A remote-desktop session takes the resolution and scale of the PC you connect from (1920×1080 at 150 % from a laptop,
  for instance). Check Settings › Display before the mouse/keyboard tests and note it with the results.
- The Phase 5 tests of the recorder's own UI (`AccessibilityTests`, `SettingsTests`, `AutosaveTests`) need no input: they open
  and close Settings and the cheat sheet through UI Automation, and `AutosaveTests` starts the recorder twice on one data folder.
- `./build.ps1 test -Monitor n` (or `WINFORMSRECORDER_TEST_MONITOR=n`) opens the end-to-end tests' SampleApp (`SampleAppFixture`) on
  monitor n: 1 is the main display, the others follow from left to right. `WindowPlacementTests` move the recorder's own window
  between monitors without input; with one monitor they are skipped.
- Integration tests copy the recorder's log to
  `tests/Recorder.IntegrationTests/bin/<configuration>/net10.0-windows/TestResults/recorder-logs/`.
- If a findings test fails on another Windows version, treat it as a new finding: spike, record F-n, adapt the code. Don't
  loosen the test. Several Phase 2 failures turned out to be the environment (a locked session, a test window someone
  closed), so rule that out first.
