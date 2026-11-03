<#
.SYNOPSIS
  Compiles generated Java against stubs of the LMSAutomationDemo base classes (stubs/) and the real Appium java-client
  7.6.0, Selenium 3.141.59 and TestNG 7.11.0 jars (spec 14, Phase 4; D-10).
.DESCRIPTION
  javac comes from JAVA_HOME, PATH, or a per-user JDK in %LOCALAPPDATA%\Microsoft\jdk-17. Each jar comes from lib/ next
  to this script, else from the local Maven repository (~/.m2/repository).
  Exit codes: 0 compiled, 1 compile errors, 2 skipped (no JDK), 3 skipped (a jar is missing).
.EXAMPLE
  ./check-java.ps1 -Source C:\temp\generated
#>
param(
    # Folder with the generated .java files, in any layout.
    [Parameter(Mandatory)]
    [string] $Source
)

$ErrorActionPreference = 'Stop'

function Find-Javac {
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += Join-Path $env:JAVA_HOME 'bin\javac.exe' }
    $onPath = Get-Command javac -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    $candidates += Join-Path $env:LOCALAPPDATA 'Microsoft\jdk-17\bin\javac.exe'
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$jars = @(
    'io/appium/java-client/7.6.0/java-client-7.6.0.jar',
    'org/seleniumhq/selenium/selenium-api/3.141.59/selenium-api-3.141.59.jar',
    'org/seleniumhq/selenium/selenium-remote-driver/3.141.59/selenium-remote-driver-3.141.59.jar',
    'org/seleniumhq/selenium/selenium-support/3.141.59/selenium-support-3.141.59.jar',
    'com/google/guava/guava/25.0-jre/guava-25.0-jre.jar',
    'org/testng/testng/7.11.0/testng-7.11.0.jar'
)

$javac = Find-Javac
if (-not $javac) {
    Write-Output 'Skipped: no JDK (set JAVA_HOME or put javac on PATH).'
    exit 2
}

$classpath = @()
foreach ($jar in $jars) {
    $local = Join-Path $PSScriptRoot ('lib\' + (Split-Path $jar -Leaf))
    $maven = Join-Path $env:USERPROFILE ('.m2\repository\' + $jar.Replace('/', '\'))
    $found = @($local, $maven) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) {
        Write-Output "Skipped: $(Split-Path $jar -Leaf) is in neither $(Join-Path $PSScriptRoot 'lib') nor the Maven repository."
        exit 3
    }
    $classpath += $found
}

$files = @(Get-ChildItem (Join-Path $PSScriptRoot 'stubs') -Recurse -Filter *.java) + @(Get-ChildItem $Source -Recurse -Filter *.java)
$classes = Join-Path ([System.IO.Path]::GetTempPath()) ('java-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $classes | Out-Null
try {
    # One argument per line in an @file: paths with spaces need no further quoting rules.
    $argumentsFile = Join-Path $classes 'javac.args'
    $arguments = @('--release', '17', '-encoding', 'UTF-8', '-nowarn', '-d', $classes, '-cp', ($classpath -join ';')) +
        ($files | ForEach-Object { $_.FullName })
    [System.IO.File]::WriteAllLines($argumentsFile, ($arguments | ForEach-Object { '"' + $_.Replace('\', '\\') + '"' }))
    Write-Output "javac: $javac"
    Write-Output "Compiling $($files.Count) files"

    # javac reports errors on stderr; Windows PowerShell would stop at the first line under 'Stop'.
    $ErrorActionPreference = 'Continue'
    & $javac "@$argumentsFile" 2>&1 | ForEach-Object { "$_" }
    $compiled = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
    if (-not $compiled) { exit 1 }
    Write-Output 'Compiled.'
    exit 0
}
finally {
    Remove-Item -Recurse -Force $classes -ErrorAction SilentlyContinue
}
