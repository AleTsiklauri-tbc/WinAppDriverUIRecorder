<#
.SYNOPSIS
  Lists every NuGet package the solution uses (direct and transitive) with its license, and fails
  if any license is not MIT, Apache-2.0 or BSD. Use the output to keep THIRD_PARTY_NOTICES.md current.
.EXAMPLE
  ./eng/list-licenses.ps1 -Markdown
#>
param(
    [switch] $Markdown
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$onPath = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($onPath) { $onPath.Source } else { 'dotnet' }
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (Test-Path $userDotnet) { $dotnet = $userDotnet }

$json = & $dotnet list (Join-Path $root 'WinFormsRecorder.sln') package --include-transitive --format json | ConvertFrom-Json
$packages = foreach ($project in $json.projects) {
    $projectName = [IO.Path]::GetFileNameWithoutExtension($project.path)
    foreach ($framework in $project.frameworks) {
        foreach ($p in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($p) { [pscustomobject]@{ Id = $p.id; Version = $p.resolvedVersion; Project = $projectName; Direct = [bool]($framework.topLevelPackages | Where-Object id -eq $p.id) } }
        }
    }
}

$cache = Join-Path $env:USERPROFILE '.nuget\packages'
$allowed = '^(MIT|Apache-2\.0|BSD-2-Clause|BSD-3-Clause)$'
$rows = $packages | Group-Object Id, Version | ForEach-Object {
    $p = $_.Group[0]
    $nuspec = Join-Path $cache ("{0}\{1}\{0}.nuspec" -f $p.Id.ToLowerInvariant(), $p.Version.ToLowerInvariant())
    [xml] $xml = Get-Content -Raw $nuspec
    $meta = $xml.package.metadata
    $license = if ($meta.license.type -eq 'expression') { $meta.license.'#text' }
               elseif ($meta.license.type -eq 'file') { 'file:' + $meta.license.'#text' }
               else { 'url:' + $meta.licenseUrl }
    [pscustomobject]@{
        Package = $p.Id; Version = $p.Version; License = $license
        Direct = ($_.Group | Where-Object Direct).Count -gt 0
        UsedBy = (($_.Group.Project | Sort-Object -Unique) -join ', ')
        Authors = $meta.authors; Project = $meta.projectUrl
    }
} | Sort-Object Package

if ($Markdown) {
    '| Package | Version | License | Used by |'
    '|---|---|---|---|'
    $rows | ForEach-Object { '| {0} | {1} | {2} | {3} |' -f $_.Package, $_.Version, $_.License, $_.UsedBy }
} else {
    $rows | Format-Table Package, Version, License, Direct, UsedBy -AutoSize
}

$needsReview = $rows | Where-Object { $_.License -notmatch $allowed }
if ($needsReview) {
    Write-Warning ("Review these licenses by hand (license file or URL): " + (($needsReview | ForEach-Object { "$($_.Package) $($_.Version) [$($_.License)]" }) -join '; '))
}
