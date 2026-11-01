# WinForms Recorder

WinForms Recorder is a locator inspector and test recorder for .NET Framework WinForms apps automated with WinAppDriver.

- Hover a control and press one hotkey. You get the most stable locator, whether it is unique, and a ready-to-paste Java line.
- Record a flow. You get Page, Steps and Test classes in the style of your own Java framework.

It replaces Microsoft's WinAppDriver UI Recorder. The original is kept unmodified in [legacy/UIRecorder](legacy/UIRecorder).

> **Status: Phase 2 (locator engine).** Choose the app to automate, hover a control and press **Ctrl+Shift+X**,
> or use **Pick** and click it. The Inspector shows the best locator, whether it is unique, why it was chosen, the other
> locators, and the Java line to paste into a page class. **Verify with WinAppDriver** asks a running WinAppDriver for
> the same counts. Recording and full code generation arrive in Phases 3–4; their toolbar buttons are visible but disabled.

## Build from source

**Requirements**

- Windows 10/11 or Windows Server 2022, x64.
- No admin rights needed.
- .NET SDK **10.0.403**; see [global.json](global.json). A per-user install is fine.
- The corporate NuGet group. [NuGet.config](NuGet.config) points only at it, because api.nuget.org is not reachable from here.

**Install the SDK per user** if `dotnet --list-sdks` does not show 10.0.x. Compare the hash with the
`sha512` of `dotnet-sdk-win-x64.zip` in the
[release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json):

```powershell
$v = '10.0.403'
curl.exe -L -o "$env:TEMP\dotnet-sdk.zip" "https://dotnetcli.blob.core.windows.net/dotnet/Sdk/$v/dotnet-sdk-$v-win-x64.zip"
(Get-FileHash -Algorithm SHA512 "$env:TEMP\dotnet-sdk.zip").Hash
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\Microsoft\dotnet" | Out-Null
tar -xf "$env:TEMP\dotnet-sdk.zip" -C "$env:LOCALAPPDATA\Microsoft\dotnet"
```

**Build, test, run.** [build.ps1](build.ps1) finds the SDK and opts out of dotnet CLI telemetry.

```powershell
./build.ps1          # build; src/ treats every warning as an error
./build.ps1 test     # unit + integration tests; integration tests open the sample app and the recorder briefly
./build.ps1 run      # start WinForms Recorder
```

Some integration tests use the real mouse and keyboard. `./build.ps1 test` runs them first, for about a minute (they put
the cursor back, and one borrows the clipboard and restores its text). While they run, the session must be unlocked and
the remote-desktop window not minimized; don't touch the mouse. Leave them out while you work:

```powershell
./build.ps1 test -NoInput
```

The tests marked `[Trait("Requires", "WinAppDriver")]` compare every generated locator with a real WinAppDriver. They are
skipped unless WinAppDriver answers at the given address (default `http://127.0.0.1:4723`) and add about 10 minutes after
the mouse tests. Meanwhile WinAppDriver keeps a **Recorder Sample** window in front: you can use other windows, but leave
that one open and don't click it, or the run fails:

```powershell
./build.ps1 test -WinAppDriverUrl http://127.0.0.1:4727
```

## Using it (Phase 2)

| To | Do |
|---|---|
| Choose the app | Click **Choose app** and pick a window. The recorder remembers the app and re-attaches when it restarts. |
| Capture a control | Hover it and press **Ctrl+Shift+X**. With no app chosen yet, this also chooses the app under the cursor. |
| Pick a control | Click **Pick** and hover: the outline turns green (unique, stable), amber (unique but text- or position-based), red (no unique locator) or dashed blue (already captured). Click to capture; the click goes to the recorder, not the app. **Esc** cancels. |
| Get the locator | The Inspector's card shows the best locator, "1 match in window ✓" or "3 matches ✗", and the Java line, e.g. `public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId("txtAmount");}`. **Copy Java line** or **Ctrl+C** copies it. |
| Use another locator | Pick one under **Other locators**; the card, the Java line and the Elements tick follow. |
| See what a locator finds | **Highlight matches** outlines every match on screen for 1.5 s. |
| Check with WinAppDriver | Start `WinAppDriver.exe`, click **Verify with WinAppDriver**. The first time, type your address (LMSAutomationDemo uses `http://127.0.0.1:4727`) and click Verify again; it is remembered. |
| Rename the variable | Edit the name at the top of the Inspector, or select the element in Elements and press **F2**. |
| Find a captured control | **Ctrl+F** filters the Elements list. |
| Change the shortcut | Edit `captureHotkey` in `%APPDATA%\WinFormsRecorder\settings.json` (a Settings page arrives in Phase 5). |

## Where things live

| Path | What |
|---|---|
| `src/Recorder.Core` | All logic, no WPF |
| `src/Recorder.App` | WPF app (`WinFormsRecorder.exe`) |
| `tests/` | Unit tests, FlaUI integration tests, and `Recorder.SampleApp`, a .NET Framework 4.8 app imitating LMS |
| `docs/ARCHITECTURE.md` | Design, legacy → new mapping, decisions and **Findings** (verified UIA facts) |
| `docs/target-style/` | Real Page/Steps/Test files the generated Java must match |
| `eng/list-licenses.ps1` | License inventory for [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |

## Privacy

- No telemetry, update checks or crash reporters. Tests enforce this, including transitive packages.
- Logs stay on this machine: `%LOCALAPPDATA%\WinFormsRecorder\logs`, kept for 7 days.
- Password fields are never read, and printable keystrokes are never recorded (Phase 3).

## License notices

Third-party components and their licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
