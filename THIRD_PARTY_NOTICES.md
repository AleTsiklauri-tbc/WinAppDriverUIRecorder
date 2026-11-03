# Third-party notices

WinForms Recorder uses the third-party material below: ideas from the WinAppDriver UI Recorder, NuGet packages (all
under MIT, Apache-2.0 or BSD licenses) and, inside the published exe, .NET itself (MIT).

## WinAppDriver UI Recorder (MIT)

[legacy/UIRecorder/](legacy/UIRecorder/) is an **unmodified** copy of `Tools/UIRecorder` from Microsoft's
[WinAppDriver](https://github.com/microsoft/WinAppDriver) repository (tag v1.1). It is kept as reference
material and is not built or shipped.

WinForms Recorder rewrites the following ideas from it in modern C#:

| Idea | Legacy source | Status |
|---|---|---|
| GUID-like value detection | `GenerateXPath.cs` (`CheckAndFixNoneStaticValue`) | Done in Phase 2 (`Locators/IdRules.cs`) |
| Dynamic digit suffix → `starts-with` prefix, narrowed so designer names such as `textBox12` stay exact | `GenerateXPath.cs` (`GenerateXPathToUiElement`) | Done in Phase 2 (`Locators/IdRules.cs`) |
| `ElementFromPoint` → ancestor chain, re-implemented with FlaUI and a cache request | `UIXPathLib/UiTreeWalk.cpp` | Done in Phase 1 (`ElementSnapshotFactory`) |

No code is copied from it; Phases 1 and 2 re-implement the ideas above from scratch. Its license applies to the copy and to any code derived from it:

```text
The MIT License (MIT)

Copyright (c) 2016 Microsoft

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## .NET 10 (inside the published exe)

`./build.ps1 publish` puts the .NET 10.0.14 runtime and WPF (`Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App`)
into `WinFormsRecorder.exe`, so that it runs where .NET isn't installed. Both are under this license:

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The runtime contains third-party code of its own under permissive licenses (MIT, BSD, Apache-2.0, zlib, Unicode and
others). They are listed in `THIRD-PARTY-NOTICES.TXT` of the `Microsoft.NETCore.App.Runtime.win-x64` 10.0.14 package
(`%USERPROFILE%\.nuget\packages\microsoft.netcore.app.runtime.win-x64\10.0.14\` after a publish) and in the
[dotnet/runtime](https://github.com/dotnet/runtime) repository. The ReadyToRun compiler (`Microsoft.NETCore.App.Crossgen2.win-x64`,
MIT) only builds the exe and is not part of it.

## NuGet packages

Generated with `./eng/list-licenses.ps1 -Markdown` (direct and transitive). Run it again after any dependency
change. Four packages ship a license file instead of an SPDX expression; each was checked by hand.

### Shipped with the recorder

Packages used by `Recorder.App` and `Recorder.Core`:

| Package | Version | License | Used by |
|---|---|---|---|
| AvalonEdit | 6.3.1.120 | MIT | Recorder.App (the code view) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Recorder.App |
| DiffPlex | 1.9.0 | Apache-2.0 | Recorder.App (the merge preview) |
| DiffPlex.Wpf | 1.9.1 | Apache-2.0 | Recorder.App (the merge preview) |
| FlaUI.Core | 5.0.0 | MIT (license file) | Recorder.App, Recorder.Core |
| FlaUI.UIA3 | 5.0.0 | MIT (license file) | Recorder.App, Recorder.Core |
| Interop.UIAutomationClient | 10.19041.0 | MIT (license file) | Recorder.App, Recorder.Core |
| Microsoft.Extensions.Configuration | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.Binder | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.Json | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | MIT | Recorder.App, Recorder.Core |
| Microsoft.Extensions.Diagnostics | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.FileProviders.Physical | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT | Recorder.App, Recorder.Core |
| Microsoft.Extensions.Logging.Configuration | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.Console | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.Debug | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.EventLog | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.EventSource | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Options | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Primitives | 10.0.14 | MIT | Recorder.App |
| Microsoft.Win32.SystemEvents | 8.0.0 | MIT | Recorder.Core |
| Scriban | 7.5.3 | BSD-2-Clause | Recorder.Core (code templates) |
| Serilog | 4.4.0 | Apache-2.0 | Recorder.App |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 | Recorder.App |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | Recorder.App |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Recorder.App |
| System.CodeDom | 8.0.0 | MIT | Recorder.Core |
| System.Configuration.ConfigurationManager | 8.0.1 | MIT | Recorder.Core |
| System.Diagnostics.EventLog | 8.0.1 | MIT | Recorder.Core |
| System.Diagnostics.PerformanceCounter | 8.0.1 | MIT | Recorder.Core |
| System.Drawing.Common | 8.0.10 | MIT | Recorder.Core |
| System.Management | 8.0.0 | MIT | Recorder.App, Recorder.Core |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | Recorder.Core |
| System.Security.Permissions | 8.0.0 | MIT | Recorder.Core |
| System.Windows.Extensions | 8.0.0 | MIT | Recorder.Core |

The `Microsoft.Extensions.Logging.*` provider packages come in through `Microsoft.Extensions.Hosting`. The recorder
does not register any of them; its only log destination is a local Serilog file. The `System.*` and `Microsoft.Win32.SystemEvents` packages come in through FlaUI 5, the UI Automation library used for capture.

Not used: **Verify** (snapshot testing). Since version 33 its binaries come with an "Open Source Maintenance Fee"
agreement instead of a plain MIT license, so the snapshot tests use a small helper of their own (ARCHITECTURE D-62).

The Java compile check (`tests/JavaCompileCheck`, never shipped) uses Appium java-client 7.6.0 and Selenium 3.141.59
(Apache-2.0), Guava 25.0 (Apache-2.0) and TestNG 7.11.0 (Apache-2.0) from Maven Central; they are not part of this
repository.

### All packages, including test-only

`Recorder.Core.Tests`, `Recorder.IntegrationTests` and `Recorder.SampleApp` are never shipped.

| Package | Version | License | Used by |
|---|---|---|---|
| AvalonEdit | 6.3.1.120 | MIT | Recorder.App |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Recorder.App |
| DiffEngine | 11.3.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| DiffPlex | 1.9.0 | Apache-2.0 | Recorder.App |
| DiffPlex.Wpf | 1.9.1 | Apache-2.0 | Recorder.App |
| EmptyFiles | 4.4.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| FlaUI.Core | 5.0.0 | MIT (license file) | Recorder.App, Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| FlaUI.UIA3 | 5.0.0 | MIT (license file) | Recorder.App, Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| Interop.UIAutomationClient | 10.19041.0 | MIT (license file) | Recorder.App, Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.CodeCoverage | 18.11.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.Extensions.Configuration | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.Binder | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.Json | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | MIT | Recorder.App, Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.Extensions.Diagnostics | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.FileProviders.Physical | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT | Recorder.App, Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.Extensions.Logging.Configuration | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.Console | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.Debug | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.EventLog | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Logging.EventSource | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Options | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.14 | MIT | Recorder.App |
| Microsoft.Extensions.Primitives | 10.0.14 | MIT | Recorder.App |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.TestPlatform.ObjectModel | 18.11.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.TestPlatform.TestHost | 18.11.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.Web.WebView2 | 1.0.4314.35 | BSD-3-Clause (license file) | Recorder.SampleApp |
| Microsoft.Win32.Registry | 5.0.0 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| Microsoft.Win32.SystemEvents | 8.0.0 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| Scriban | 7.5.3 | BSD-2-Clause | Recorder.App, Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| Serilog | 4.4.0 | Apache-2.0 | Recorder.App |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 | Recorder.App |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | Recorder.App |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Recorder.App |
| Shouldly | 4.3.0 | BSD-3-Clause | Recorder.Core.Tests, Recorder.IntegrationTests |
| System.CodeDom | 8.0.0 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Configuration.ConfigurationManager | 8.0.1 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Diagnostics.EventLog | 8.0.1 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Diagnostics.PerformanceCounter | 8.0.1 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Drawing.Common | 8.0.10 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Management | 8.0.0 | MIT | Recorder.App, Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Security.AccessControl | 6.0.1 | MIT | Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Security.Permissions | 8.0.0 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| System.Windows.Extensions | 8.0.0 | MIT | Recorder.Core, Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.analyzers | 2.2.0 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.runner.visualstudio | 4.0.1 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.v3.assert | 4.0.2 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.v3.common | 4.0.2 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.v3.core.mtp-off | 4.0.2 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.v3.extensibility.core | 4.0.2 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.v3.mtp-off | 4.0.2 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.v3.runner.common | 4.0.2 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
| xunit.v3.runner.inproc.console | 4.0.2 | Apache-2.0 | Recorder.Core.Tests, Recorder.IntegrationTests |
