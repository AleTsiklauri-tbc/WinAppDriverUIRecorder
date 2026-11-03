# User guide

WinForms Recorder finds locators for the controls of a WinForms app and records flows as Java (or C#) for WinAppDriver
tests. This page covers all of it; **F1** in the recorder lists every shortcut as it is set now.

## Start

- Run `WinFormsRecorder.exe` from any folder. It needs no installation, no admin rights and no .NET. On its first start
  it unpacks WPF's native libraries (about 8 MB) to `%TEMP%\.net\WinFormsRecorder`.
- Click **Choose app** and pick the app's window. The recorder remembers the app and attaches again whenever it starts.
  With no app chosen, the capture shortcut chooses the app under the cursor.

## Capture a control

| To | Do |
|---|---|
| Capture the control under the cursor | Hover it and press **Ctrl+Shift+X**. |
| Pick with the mouse | Click **Pick** and hover. The outline is green (unique, stable), amber (unique but text- or position-based), red (no unique locator) or dashed blue (already captured); an icon in its label says the same. Click to capture: the click goes to the recorder, not the app. **Esc** cancels. |
| Copy the locator | The Inspector shows the best locator, "1 match in window ✓" or "3 matches ✗", and the Java line, e.g. `public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId("txtAmount");}`. **Copy Java line** or **Ctrl+C** copies it. |
| Use another locator | Pick one under **Other locators**; the card, the Java line and the Elements tick follow. |
| See what a locator finds | **Highlight matches** outlines every match on screen for 1.5 s. |
| Check with WinAppDriver | Start `WinAppDriver.exe` and click **Verify with WinAppDriver**. The first time, type its address (LMSAutomationDemo uses `http://127.0.0.1:4727`) and click Verify again; it is remembered. |
| Rename the variable | Edit the name at the top of the Inspector, or press **F2** in the Elements or Steps list. |
| Find a captured control | **Ctrl+F** filters the Elements list. |

## Record a flow

- Press **F9** (or **Record**) and use the app as usual. The main window steps aside and a mini-bar stays on top with the
  time, the step count and the last step. **F9** or **Stop** ends the recording and shows the Steps tab.
- **F10** pauses and resumes. It is registered only while recording.
- A typed value is recorded when you leave the input, from its value, never from keystrokes. A password becomes a
  test-data step: `Type the test value passwordInput into Password`. Its text is never read.
- Clicks, drop-down choices, check boxes, tabs, menus (`Click File › Export › PDF`), dialogs and grid cells
  (`Click row 2, “Status” in loansGrid`) become one readable step each.
- To add a check, press **Ctrl+Shift+A** (or **Assert**) and click a control. The click doesn't reach the app. Pick
  "Value equals “…”", "Is checked", "Is enabled" or "Is visible" and press **Enter**; **Esc** cancels.

## Edit steps

In the Steps tab: **Enter** edits the step's value or comment, **Del** deletes, **Ctrl+D** duplicates, **Alt+↑** and
**Alt+↓** (or drag) move the step, and Shift or Ctrl selects several. **Ctrl+Z** and **Ctrl+Y** undo and redo edits,
renames and locator choices.

## Get the code

The code pane, right of the Inspector or below it, follows every capture, step, rename and locator choice.

| Tab | Shows |
|---|---|
| **Pages** | One page class per form, with its locators. Edit **Class name** and press **Enter** to rename it; pages are named after the form's AutomationId (`frmLoan` → `LoanPage`), never after a window title. |
| **Steps** | One Steps class per page, with fluent actions. |
| **Test** | The test for the recording (TestNG in the Java profile). A password is never in it: the test passes `ConfigReader.getProperty("passwordInput")`. |
| **Element** | The selected control's line. |

- **Copy** (**Ctrl+Shift+C**) copies the class shown, **Save as…** saves it, and **Export code** (**Ctrl+E**) writes every class
  into a folder.
- **Merge into file…** picks an existing class, shows both versions side by side, and adds only the locators, methods and
  imports it lacks. The old file is kept as `.bak`. When a file can't be read safely, it offers to copy the new members instead.
- The profile picker at the top of the pane switches between Java and C#. To change the generated code, see
  [TEMPLATES.md](TEMPLATES.md).

## Save and restore

- **Ctrl+S**, **Ctrl+Shift+S** and **Ctrl+O** save, save as and open recordings (`*.wfrec.json`). They are also under
  **Export ▾**, with **Open recent** for the last eight.
- Unsaved work is kept 2 s after every change. After a crash, or closing without saving, the next start asks "Restore
  your unsaved recording from 14:32?". Saving or opening a recording clears it.

## Settings

**Settings** (⚙) applies each change at once. It doesn't open while you record.

| Section | Holds |
|---|---|
| General | Minimize the window while recording, the code pane right of the Inspector or below it, the theme (as in Windows, light or dark) |
| Shortcuts | The four shortcuts that work in every app. Click a box and press the new keys: keys that would stop working elsewhere, or that another app holds, are refused with the reason. **Default** goes back. |
| Recording | Record special keys (Enter, Tab, Esc, F1–F12, Delete, arrows, Ctrl and Alt shortcuts), record every typed value as test data, record mouse wheel scrolls as comments |
| Code generation | The profile, **Open profiles folder**, **Duplicate profile** |
| WinAppDriver | The address for **Verify with WinAppDriver**, and whether a WinAppDriver on another computer is allowed |
| Advanced | Detailed logs, the UI Automation timeout (after a restart), **Open logs folder** |

## When something goes wrong

| You see | Do |
|---|---|
| "This app runs as administrator. Restart WinForms Recorder as administrator to inspect it." | Click **Restart as administrator**: Windows doesn't let a normal program read an elevated one. |
| "… is not responding…" | The app is busy. The recorder waits for it and carries on when it answers. |
| "… closed. Waiting for it to start again…" | Start the app again; the recorder attaches by itself. |
| "… is already used by another app…" when the recorder starts | Another program holds that shortcut. Click **Choose a shortcut**, or use the toolbar button. |
| "WinAppDriver isn't running at …" | Start `WinAppDriver.exe` (Developer Mode must be on), or correct the address in Settings › WinAppDriver. |
| "Only addresses on this computer are allowed." | Tick **Allow a WinAppDriver on another computer** in Settings › WinAppDriver. |
| The exe doesn't start on a PC that blocks programs in `%TEMP%` | Set the environment variable `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to a folder where programs may run, then start it again. |

With a problem report, send the logs: Settings › Advanced › **Open logs folder**. They hold timings and statuses, never
names, values, window titles or passwords, and are kept for 7 days.

## Files

| What | Where |
|---|---|
| Recordings | `*.wfrec.json`, wherever you save them |
| Settings | `%APPDATA%\WinFormsRecorder\settings.json` |
| Your profiles | `%APPDATA%\WinFormsRecorder\profiles` |
| Unsaved recordings | `%LOCALAPPDATA%\WinFormsRecorder\autosave` |
| Logs | `%LOCALAPPDATA%\WinFormsRecorder\logs` |

## Privacy

Nothing leaves your PC: no telemetry, no update checks, and **Verify with WinAppDriver** asks only this computer unless
you allow another. Password fields are never read, and keystrokes are never recorded as text: only Enter, Tab, Esc,
F1–F12, Delete, the arrows and Ctrl or Alt shortcuts, and in a password field only Enter and Tab. Recordings, logs and
generated code never contain window titles, which in LMS name the customer.
