# Release process for this plugin (per-plugin copy of ValheimModInstaller\release\release.ps1).
#
#   release\Release.bat                 (double-click)
#   powershell -File release\release.ps1 [-BuildOnly] [-SkipBuild] [-Bump patch|minor|major] [-Changes "text"]
#
# 1. Checks the repo is committed and pushed to origin/main (plugin.json/dist DLL are served from
#    main and releases are tagged from main).
# 2. Bumps the version (patch by default) in plugin.json and src\Plugin.cs, optionally adding
#    -Changes as the changelog entry for the new version.
# 3. Rebuilds the plugin with `dotnet build -c Release`.
# 4. Commits "Release vX.Y.Z" and pushes it to origin/main.
# 5. Runs release\make_release.py: builds the zip into release\out and publishes the GitHub release.
#
# -BuildOnly skips steps 1, 2, 4 and publishing: it just builds the package with the current version.

param(
    [switch]$BuildOnly,                                    # build the package but don't bump/commit/publish
    [switch]$SkipBuild,                                    # don't run dotnet build (only with -BuildOnly)
    [ValidateSet('patch', 'minor', 'major')][string]$Bump = 'patch',
    [string]$Changes = ''                                  # changelog line for the new version
)

$ErrorActionPreference = 'Stop'
$pluginDir = Split-Path -Parent $PSScriptRoot
$pluginName = Split-Path -Leaf $pluginDir
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host "`nRELEASE STOPPED: $text" -ForegroundColor Red; exit 1 }

function Find-Exe($name, $fallbacks) {
    $c = Get-Command $name -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($f in $fallbacks) { if (Test-Path $f) { return $f } }
    return $null
}

# Fails if the repo has uncommitted changes or commits that aren't on origin.
function Assert-GitClean($dir) {
    $name = Split-Path -Leaf $dir
    if (-not (Test-Path (Join-Path $dir '.git'))) { Fail "$name is not a git repo." }
    $dirty = git -C $dir status --porcelain
    if ($dirty) { Fail "$name has uncommitted changes:`n$($dirty -join "`n")`nCommit and push them first." }
    git -C $dir fetch --quiet origin 2>$null
    $ahead = git -C $dir rev-list --count '@{u}..HEAD' 2>$null
    if ($LASTEXITCODE -ne 0) { Fail "$name has no upstream branch. Push it first (git push -u origin main)." }
    if ([int]$ahead -gt 0) { Fail "$name has $ahead unpushed commit(s). Run: git -C `"$dir`" push" }
    $behind = git -C $dir rev-list --count 'HEAD..@{u}' 2>$null
    if ([int]$behind -gt 0) { Fail "$name is $behind commit(s) behind origin. Run: git -C `"$dir`" pull" }
    Write-Host "  $name OK"
}

function Read-Text($path) { [System.IO.File]::ReadAllText($path) }
function Write-Text($path, $text) { [System.IO.File]::WriteAllText($path, $text, $utf8) }

function Invoke-Build {
    $csproj = Get-ChildItem $pluginDir -Filter *.csproj | Select-Object -First 1
    if (-not $csproj) { Fail "no .csproj in $pluginDir" }
    $dotnet = Find-Exe 'dotnet' @("$env:ProgramFiles\dotnet\dotnet.exe")
    if (-not $dotnet) { Fail '.NET SDK not found (winget install Microsoft.DotNet.SDK.8).' }
    & $dotnet build $csproj.FullName -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Fail "$pluginName failed to build." }
}

$python = Find-Exe 'python' @("$env:LOCALAPPDATA\Programs\Python\Python312\python.exe")
if (-not $python) { $python = Find-Exe 'py' @() }
if (-not $python) { Fail 'Python not found.' }

$pluginJson = Join-Path $pluginDir 'plugin.json'
$pluginCs   = Join-Path $pluginDir 'src\Plugin.cs'

if ($BuildOnly) {
    if (-not $SkipBuild) {
        Step "Building $pluginName"
        # Builds aren't byte-identical, so only rebuild when the source is newer than dist\ -
        # otherwise a build-only run leaves an uncommitted DLL change that blocks the next release.
        $csproj = Get-ChildItem $pluginDir -Filter *.csproj | Select-Object -First 1
        $dll = Get-ChildItem (Join-Path $pluginDir 'dist') -Filter *.dll -ErrorAction SilentlyContinue | Select-Object -First 1
        $inputs = @(Get-ChildItem (Join-Path $pluginDir 'src') -Recurse -File -ErrorAction SilentlyContinue) + @($csproj)
        $newest = ($inputs | Measure-Object LastWriteTimeUtc -Maximum).Maximum
        if ($dll -and $dll.LastWriteTimeUtc -ge $newest) { Write-Host "  up to date" } else { Invoke-Build }
    }
} else {
    Step 'Checking repo is committed and pushed'
    Assert-GitClean $pluginDir

    Step "Bumping version ($Bump)"
    $json = Read-Text $pluginJson
    # The plugin's own version is the first top-level "version" (the nested one is the Valheim version).
    $m = [regex]::Match($json, '(?m)^  "version":\s*"(\d+)\.(\d+)\.(\d+)"')
    if (-not $m.Success) { Fail 'Could not find "version": "X.Y.Z" in plugin.json.' }
    $old = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)"
    [int]$maj = $m.Groups[1].Value; [int]$min = $m.Groups[2].Value; [int]$pat = $m.Groups[3].Value
    switch ($Bump) {
        'major' { $maj++; $min = 0; $pat = 0 }
        'minor' { $min++; $pat = 0 }
        'patch' { $pat++ }
    }
    $new = "$maj.$min.$pat"
    $json = $json.Substring(0, $m.Index) + "  `"version`": `"$new`"" + $json.Substring($m.Index + $m.Length)
    if ($Changes) {
        $entry = ($Changes -replace '\\', '\\' -replace '"', '\"')
        $json = [regex]::Replace($json, '("changelog":\s*\{)(\r?\n)', "`$1`$2    `"$new`": `"$entry`",`$2", 1)
    }
    Write-Text $pluginJson $json

    $cs = Read-Text $pluginCs
    $csNew = [regex]::Replace($cs, '(Version\s*=\s*")[^"]+(")', "`${1}$new`${2}", 1)
    if ($csNew -eq $cs) { Fail 'Could not find Version = "..." in src\Plugin.cs.' }
    Write-Text $pluginCs $csNew
    Write-Host "  $old -> $new"

    Step "Building $pluginName $new"
    Invoke-Build

    Step "Committing and pushing v$new"
    git -C $pluginDir add -A
    git -C $pluginDir commit -q -m "Release v$new"
    if ($LASTEXITCODE -ne 0) { Fail 'git commit failed.' }
    git -C $pluginDir push -q origin HEAD
    if ($LASTEXITCODE -ne 0) { Fail 'git push failed (the version bump is committed locally).' }
    Write-Host "  pushed"
}

Step ($(if ($BuildOnly) { 'Building package' } else { 'Building and publishing package' }))
$makeArgs = @((Join-Path $PSScriptRoot 'make_release.py'))
if ($BuildOnly) { $makeArgs += '--build-only' }
& $python @makeArgs
if ($LASTEXITCODE -ne 0) { Fail 'make_release.py failed (see above).' }

Write-Host "`nRelease process finished." -ForegroundColor Green
