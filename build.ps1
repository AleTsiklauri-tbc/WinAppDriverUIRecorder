<#
.SYNOPSIS
  Builds, tests or runs WinForms Recorder with the .NET SDK pinned in global.json.
.EXAMPLE
  ./build.ps1             # build the solution
  ./build.ps1 test        # build and run all tests (the integration tests open windows briefly)
  ./build.ps1 test -NoInput   # skip the tests that use the real mouse and keyboard
  ./build.ps1 test -WinAppDriverUrl http://127.0.0.1:4727   # also compare with a running WinAppDriver (about 10 more minutes)
  ./build.ps1 test -Monitor 2   # with the SampleApp on the second monitor (spec 14: two monitors at 100 % and 150 %)
  ./build.ps1 run         # start the recorder
  ./build.ps1 publish     # one self-contained exe: artifacts\publish\win-x64\WinFormsRecorder.exe
  ./build.ps1 test -Published   # the integration tests against that exe instead of the build output
#>
param(
    [ValidateSet('build', 'test', 'run', 'publish')]
    [string] $Task = 'build',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    # Skips the end-to-end tests that move the cursor and send keys (Category=Input).
    [switch] $NoInput,
    # Where the [Trait("Requires", "WinAppDriver")] tests find WinAppDriver. They are skipped when nothing answers there.
    [string] $WinAppDriverUrl,
    # Puts the SampleApp on this monitor for the tests: 1 is the main display, the others follow from left to right.
    [int] $Monitor,
    # Runs the integration tests against the published exe (./build.ps1 publish first) instead of the build output.
    [switch] $Published
)

$publishedExe = Join-Path $PSScriptRoot 'artifacts\publish\win-x64\WinFormsRecorder.exe'

Set-Location $PSScriptRoot

# The dotnet CLI sends usage data by default; nothing in this project may.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# Prefer a dotnet on PATH; fall back to the per-user install (no admin rights needed).
$onPath = Get-Command dotnet -ErrorAction SilentlyContinue
$candidates = @(
    $(if ($onPath) { $onPath.Source })
    (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
) | Where-Object { $_ -and (Test-Path $_) }

$dotnet = $null
foreach ($candidate in $candidates) {
    # 'dotnet --version' honours global.json and fails if the pinned SDK is missing.
    & $candidate --version *> $null
    if ($LASTEXITCODE -eq 0) { $dotnet = $candidate; break }
}
if (-not $dotnet) {
    $required = (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version
    throw ".NET SDK $required was not found. See 'Build from source' in README.md for a per-user install."
}

# Debug builds are framework-dependent; point them at the runtime that ships with this SDK.
$env:DOTNET_ROOT = Split-Path $dotnet

switch ($Task) {
    'build' { & $dotnet build WinFormsRecorder.sln -c $Configuration }
    'test' {
        if ($WinAppDriverUrl) { $env:WINFORMSRECORDER_WINAPPDRIVER_URL = $WinAppDriverUrl }
        if ($Monitor) { $env:WINFORMSRECORDER_TEST_MONITOR = $Monitor }
        if ($Published) {
            if (-not (Test-Path $publishedExe)) { throw "$publishedExe not found. Run ./build.ps1 publish first." }
            $env:WINFORMSRECORDER_RECORDER_EXE = $publishedExe
        }
        & $dotnet build WinFormsRecorder.sln -c $Configuration
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        # The tests that use the mouse and keyboard run first (about a minute) while you are still at the desk:
        # once the session locks or the remote-desktop window is minimized, Windows blocks simulated input.
        $failed = $false
        if (-not $NoInput) {
            & $dotnet test tests/Recorder.IntegrationTests -c $Configuration --no-build --filter 'Category=Input'
            $failed = $LASTEXITCODE -ne 0
        }

        # One test project after the other: both time things against the spec's budgets, and must not compete for the CPU.
        & $dotnet test tests/Recorder.Core.Tests -c $Configuration --no-build
        $failed = $failed -or $LASTEXITCODE -ne 0
        & $dotnet test tests/Recorder.IntegrationTests -c $Configuration --no-build --filter 'Category!=Input'
        exit $(if ($failed -or $LASTEXITCODE -ne 0) { 1 } else { 0 })
    }
    'run' { & $dotnet run --project src/Recorder.App -c $Configuration }
    'publish' {
        # Spec 3: one self-contained exe; the settings are in src/Recorder.App/Properties/PublishProfiles/win-x64.pubxml.
        $folder = Split-Path $publishedExe
        if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
        & $dotnet publish src/Recorder.App -c Release -p:PublishProfile=win-x64
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        # Nothing may sit next to the exe: whoever copies it alone would leave it behind.
        $extra = Get-ChildItem $folder | Where-Object Name -ne (Split-Path $publishedExe -Leaf)
        if ($extra) { throw "The publish folder should hold only the exe, but also has: $($extra.Name -join ', ')" }
        '{0} ({1:0} MB)' -f $publishedExe, ((Get-Item $publishedExe).Length / 1MB)
    }
}
exit $LASTEXITCODE
