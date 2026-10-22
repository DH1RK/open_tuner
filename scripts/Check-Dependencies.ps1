<#
.SYNOPSIS
    Checks the external pieces OpenTuner is built and shipped with for newer versions (fork tooling, dh1rk/open_tuner).

.DESCRIPTION
    Run it before a release (it changes nothing, it only reports). The table has three parts: External
    (FFmpeg, libmpv: downloaded and installed by hand), NuGet (restored automatically, version in
    opentuner.csproj) and Consistency (a comparison of the repo's own files).

      1. FFmpeg: the version the build bundles (FfmpegSharedBuildPath in opentuner.csproj) and the version
         SETUP.md says it was tested with, against the newest tag n<major>.* of FFmpeg/FFmpeg, a newer major
         version, and the matching "full_build-shared" release of GyanD/codexffmpeg. FlyleafLib needs a shared
         build whose *major* version matches Flyleaf.FFmpeg.ABI, so a newer major version must not be
         taken over before FlyleafLib supports it (check the NuGet packages below).
      2. NuGet: "dotnet list package --outdated" for the solution, FlyleafLib and Flyleaf.FFmpeg.ABI marked.
      3. libmpv: the newest mpv-dev-x86_64-<date>-git-<hash>.7z (not -v3) of shinchiro/mpv-player-windows on
         SourceForge against the build SETUP.md says it was tested with. SourceForge may refuse scripts (403);
         then the script says so and prints the link.

    "Used / bundled" is what the build uses (FFmpeg: opentuner.csproj, that is what the zip contains; NuGet:
    the resolved version). "Documented as tested" is only what SETUP.md says (a statement, not a check).
    The part "This machine" reads the Playback Paths of bin\Debug and bin\Release (settings\open_tuner_settings.json,
    personal, not in the repo): what the program started from Visual Studio really loads.
    The result is a short table: OK / NEWER (check it) / DIFFERS (files disagree) / MISSING / UNKNOWN. The exit code is always 0 when the script
    itself ran; it is advice, not a gate. If something is NEWER: read the change list (links are printed), test
    it, then update SETUP.md, Utilities\PlaybackPathsSetupForm.cs, opentuner.csproj and MainSettings.cs (see
    "Tested against" in SETUP.md).

.PARAMETER SkipNuGet
    Skip "dotnet list package --outdated" (needs a restore and the network, takes a while).

.PARAMETER Details
    Numbers (column #) of the table rows to show the changes for: the versions in between, the release notes
    of the package, the links to its project / releases page, or for FFmpeg the Changelog and the comparison.
    Without this parameter a console asks for the numbers after the table (Enter = done).

.PARAMETER Licenses
    Adds the column "License": the license of every NuGet package (from its .nuspec on nuget.org: the SPDX
    expression, or the license file / URL when there is none) and a short summary. Needs one request per package.
    The "check" mark is a rough guide (is it a well-known license that can be combined with the GPL-3.0 of
    OpenTuner?), not a legal opinion. For the FFmpeg and libmpv builds the column shows the license named in
    THIRD-PARTY-NOTICES.md for the builds of the zip; a build you use instead may have another license, look at it.

.PARAMETER Open
    Open the main change page of the rows chosen with -Details (or at the prompt) in the browser.

.EXAMPLE
    .\scripts\Check-Dependencies.ps1

.EXAMPLE
    .\scripts\Check-Dependencies.ps1 -Details "3,9" -Open
#>
param(
    [switch]$SkipNuGet,
    [string]$Details,  # a string on purpose: powershell -File passes "3,19" as one argument
    [switch]$Open,

    [switch]$Licenses
)

$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $PSScriptRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$userAgent = "Mozilla/5.0 (Check-Dependencies.ps1 of dh1rk/open_tuner)"

$results = New-Object System.Collections.Generic.List[object]
$script:CurrentSection = "External"  # External | NuGet | Consistency, set before each block below
function Add-Result([string]$What, [string]$Have, [string]$Latest, [string]$State, [string]$Hint, [string]$Kind = "", [string]$Key = "", [string]$Documented = "") {
    $results.Add([pscustomobject]@{ What = $What; Have = $Have; Latest = $Latest; State = $State; Hint = $Hint; Kind = $Kind; Key = $Key; Documented = $Documented; License = ""; Section = $script:CurrentSection })
}

function Get-JsonFromGithub([string]$Url) {
    try {
        return Invoke-RestMethod -Uri $Url -UserAgent $userAgent -Headers @{ Accept = "application/vnd.github+json" } -TimeoutSec 30
    }
    catch {
        Write-Warning "Could not read ${Url}: $($_.Exception.Message)"
        return $null
    }
}

function Compare-VersionStrings([string]$A, [string]$B) {
    # -1 if A < B, 0 if equal, 1 if A > B; "9.0" counts as "9.0.0"
    $pa = ($A -split '\.') | ForEach-Object { [int]$_ }
    $pb = ($B -split '\.') | ForEach-Object { [int]$_ }
    for ($i = 0; $i -lt [Math]::Max($pa.Count, $pb.Count); $i++) {
        $x = if ($i -lt $pa.Count) { $pa[$i] } else { 0 }
        $y = if ($i -lt $pb.Count) { $pb[$i] } else { 0 }
        if ($x -lt $y) { return -1 }
        if ($x -gt $y) { return 1 }
    }
    return 0
}

# what the repo says it uses / was tested with
$csproj = Get-Content (Join-Path $repoRoot "opentuner.csproj") -Raw
$setup = Get-Content (Join-Path $repoRoot "SETUP.md") -Raw

$bundled = [regex]::Match($csproj, 'FfmpegSharedBuildPath[^>]*>[^<]*ffmpeg-(\d+\.\d+(?:\.\d+)?)-').Groups[1].Value
$testedFfmpeg = [regex]::Match($setup, 'tested against FFmpeg (\d+\.\d+(?:\.\d+)?)').Groups[1].Value
$testedMpv = [regex]::Match($setup, 'Tested against: `?(mpv-dev-x86_64-\d{8}-git-[0-9a-f]+)').Groups[1].Value

Write-Host "FFmpeg: bundled by the build (opentuner.csproj): $bundled, tested according to SETUP.md: $testedFfmpeg"

$ffmpegLatest = ""  # the newest FFmpeg of the same major version; kept apart because the NuGet loop reuses $latest
# 1. FFmpeg ------------------------------------------------------------------------------------------------
# "Have" is what the build bundles (opentuner.csproj); what SETUP.md calls tested is only a statement (column "Documented")
$ref = if ($bundled) { $bundled } else { $testedFfmpeg }
if (-not $ref) {
    Add-Result "FFmpeg" "?" "?" "UNKNOWN" "no version found in opentuner.csproj / SETUP.md"
}
else {
    $major = [int]($ref -split '\.')[0]
    $tags = @(git ls-remote --tags --refs https://github.com/FFmpeg/FFmpeg.git 2>$null |
        ForEach-Object { if ($_ -match 'refs/tags/n(\d+\.\d+(?:\.\d+)?)$') { $Matches[1] } })
    if ($tags.Count -eq 0) {
        Add-Result "FFmpeg" $ref "?" "UNKNOWN" "git ls-remote failed (network? git in PATH?)"
    }
    else {
        $sameMajor = @($tags | Where-Object { [int]($_ -split '\.')[0] -eq $major })
        $latest = $sameMajor | Sort-Object { [version]($_ + $(if (($_ -split '\.').Count -eq 2) { '.0' } else { '' })) } | Select-Object -Last 1
        $ffmpegLatest = $latest
        $newerMajor = @($tags | Where-Object { [int]($_ -split '\.')[0] -gt $major } |
            ForEach-Object { ($_ -split '\.')[0] } | Sort-Object -Unique)

        if ((Compare-VersionStrings $ref $latest) -lt 0) {
            Add-Result "FFmpeg $major.x" $ref $latest "NEWER" "https://github.com/FFmpeg/FFmpeg/compare/n$ref...n$latest" "ffmpeg" $latest $testedFfmpeg
            $gyan = Get-JsonFromGithub "https://api.github.com/repos/GyanD/codexffmpeg/releases/tags/$latest"
            if ($gyan -and ($gyan.assets | Where-Object { $_.name -eq "ffmpeg-$latest-full_build-shared.7z" })) {
                Add-Result "  GyanD build" "-" "ffmpeg-$latest-full_build-shared" "OK" "https://github.com/GyanD/codexffmpeg/releases/tag/$latest"
            }
            else {
                Add-Result "  GyanD build" "-" "-" "UNKNOWN" "no full_build-shared of $latest at https://github.com/GyanD/codexffmpeg/releases (not built yet?)"
            }
        }
        else {
            Add-Result "FFmpeg $major.x" $ref $latest "OK" "" "" "" $testedFfmpeg
        }

        if ($newerMajor.Count -gt 0) {
            Add-Result "FFmpeg major" "$major" ($newerMajor -join ", ") "NEWER" "only usable when FlyleafLib / Flyleaf.FFmpeg.ABI support it (see NuGet below)"
        }
    }
}

# Consistency: the FFmpeg version has to be the same in every file that names it (the csproj is the reference,
# it decides what the zip contains)
if ($bundled) {
    $script:CurrentSection = "Consistency"
    $differs = 0
    foreach ($file in @("MainSettings.cs", "Utilities\PlaybackPathsSetupForm.cs", "SETUP.md", "THIRD-PARTY-NOTICES.md")) {
        $text = Get-Content (Join-Path $repoRoot $file) -Raw
        $found = @()
        foreach ($pattern in @('ffmpeg-(\d+\.\d+(?:\.\d+)?)-full_build-shared', 'codexffmpeg/releases/tag/(\d+\.\d+(?:\.\d+)?)', 'tested against FFmpeg (\d+\.\d+(?:\.\d+)?)', 'FFmpeg/FFmpeg/tree/n(\d+\.\d+(?:\.\d+)?)')) {
            $found += @([regex]::Matches($text, $pattern) | ForEach-Object { $_.Groups[1].Value })
        }
        $found = @($found | Sort-Object -Unique)
        if ($found.Count -gt 0 -and (@($found | Where-Object { $_ -ne $bundled }).Count -gt 0)) {
            $differs++
            Add-Result "FFmpeg in $file" $bundled "-" "DIFFERS" "names $($found -join ', '), opentuner.csproj bundles $bundled" "" "" ($found -join ", ")
        }
    }
    if ($differs -eq 0) {
        Add-Result "FFmpeg in the repo files" $bundled "-" "OK" "opentuner.csproj, MainSettings.cs, the setup dialog, SETUP.md and THIRD-PARTY-NOTICES.md name the same version"
    }
    $script:CurrentSection = "External"
}

$script:CurrentSection = "NuGet"
# 2. NuGet -------------------------------------------------------------------------------------------------
if ($SkipNuGet) {
    Add-Result "NuGet packages" "-" "-" "UNKNOWN" "skipped (-SkipNuGet)"
}
else {
    Write-Host "NuGet: dotnet list package, with and without --outdated (this takes a moment) ..."
    $sln = Join-Path $repoRoot "opentuner.sln"
    $env:DOTNET_CLI_UI_LANGUAGE = "en"  # the column parsing below expects the English output
    $out = @(dotnet list $sln package --outdated 2>&1)
    $packageLines = @($out | Where-Object { $_ -match '^\s*>\s+\S+\s+\S+\s+\S+\s+\S+' })
    $allOut = @(dotnet list $sln package 2>&1)
    $allLines = @($allOut | Where-Object { $_ -match '^\s*>\s+\S+\s+\S+\s+\S+\s*$' })

    if ($allLines.Count -eq 0 -and $packageLines.Count -eq 0) {
        Add-Result "NuGet packages" "?" "?" "UNKNOWN" "dotnet list package failed (restore / network?)"
        $out | Select-Object -First 8 | ForEach-Object { Write-Host "  $_" }
    }
    else {
        # the packages with a newer version ("--outdated"): name -> Latest column
        $outdated = @{}
        foreach ($line in $packageLines) {
            $f = ($line -replace '^\s*>\s+', '') -split '\s+'
            # name, requested, resolved, latest (the columns of "dotnet list package --outdated")
            $latest = $f[3]
            if ($line -match 'not found') { $latest = "(no stable version on nuget.org)" }
            $outdated[$f[0]] = $latest
        }

        # all top-level packages, as listed: the version in use, and Latest (the same one when nothing newer exists)
        $names = @()
        foreach ($line in $allLines) {
            $f = ($line -replace '^\s*>\s+', '') -split '\s+'
            $name = $f[0]; $have = $f[2]
            $names += $name
            if ($outdated.ContainsKey($name)) {
                $latest = $outdated[$name]
                $hint = if ($name -match 'Flyleaf') { "decides the FFmpeg major version - read its release notes" } else { "" }
                if ($latest -match '^\d+' -and $have -match '^\d+' -and ($latest -split '\.')[0] -ne ($have -split '\.')[0]) {
                    $hint = ("major version change (breaking changes possible). " + $hint).Trim()
                }
                $state = if ($latest -eq "(no stable version on nuget.org)") { "UNKNOWN" } else { "NEWER" }
                Add-Result $name $have $latest $state $hint "nuget" $name
            }
            else {
                Add-Result $name $have $have "OK" "" "nuget" $name
            }
        }

        # a package that only shows up in the outdated list (should not happen): do not lose it
        foreach ($name in $outdated.Keys) {
            if ($names -notcontains $name) { Add-Result $name "?" $outdated[$name] "NEWER" "" "nuget" $name }
        }
    }
}

$script:CurrentSection = "External"
# 3. libmpv ------------------------------------------------------------------------------------------------
$mpvUrl = "https://sourceforge.net/projects/mpv-player-windows/files/libmpv/"
try {
    $rss = Invoke-WebRequest -Uri "https://sourceforge.net/projects/mpv-player-windows/rss?path=/libmpv" -UserAgent $userAgent -UseBasicParsing -TimeoutSec 30
    $builds = @([regex]::Matches($rss.Content, 'mpv-dev-x86_64-(\d{8})-git-([0-9a-f]+)\.7z') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    $latestMpv = ($builds | Sort-Object { [regex]::Match($_, '\d{8}').Value } | Select-Object -Last 1) -replace '\.7z$', ''
    if (-not $latestMpv) {
        Add-Result "libmpv" "-" "?" "UNKNOWN" "no build found in the SourceForge feed, look at $mpvUrl" "" "" $testedMpv
    }
    elseif ($latestMpv -eq $testedMpv) {
        Add-Result "libmpv" "-" $latestMpv "OK" "" "" "" $testedMpv
    }
    else {
        Add-Result "libmpv" "-" $latestMpv "NEWER" $mpvUrl "mpv" $latestMpv $testedMpv
    }
}
catch {
    Add-Result "libmpv" "-" "?" "UNKNOWN" "SourceForge refused ($($_.Exception.Message)); look at $mpvUrl" "" "" $testedMpv
}

# 4. This machine: Playback Paths in the settings of the Debug / Release build --------------------------------
# Personal settings (bin\<Configuration>\settings\open_tuner_settings.json, not in the repo). They decide what
# the program started from Visual Studio really loads, whatever opentuner.csproj bundles for the zip.
$script:CurrentSection = "Machine"
foreach ($configuration in @("Debug", "Release")) {
    $settingsFile = Join-Path $repoRoot "bin\$configuration\settings\open_tuner_settings.json"
    if (-not (Test-Path $settingsFile)) { continue }
    try { $mainSettings = Get-Content $settingsFile -Raw | ConvertFrom-Json } catch { continue }

    $ffPath = [string]$mainSettings.ffmpeg_path
    $ffVersion = [regex]::Match($ffPath, 'ffmpeg-(\d+\.\d+(?:\.\d+)?)-').Groups[1].Value
    if ($ffPath) {
        $ffExists = [bool](Get-ChildItem -Path $ffPath -Filter "avcodec-*.dll" -ErrorAction SilentlyContinue)
        if (-not $ffExists) { $state = "MISSING"; $hint = "no avcodec-*.dll in $ffPath" }
        elseif ($ffVersion -and $ffmpegLatest -and (Compare-VersionStrings $ffVersion $ffmpegLatest) -lt 0) { $state = "NEWER"; $hint = $ffPath }
        else { $state = "OK"; $hint = $ffPath }
        Add-Result "ffmpeg Path ($configuration)" $(if ($ffVersion) { $ffVersion } else { "?" }) $ffmpegLatest $state $hint
    }

    $mpvPath = [string]$mainSettings.libmpv_path
    if ($mpvPath) {
        $mpvBuild = [regex]::Match($mpvPath, 'mpv-dev-x86_64-\d{8}(?:-git-[0-9a-f]+)?').Value  # older folders have no -git-<hash>
        if (-not (Test-Path (Join-Path $mpvPath "libmpv-2.dll"))) { $state = "MISSING"; $hint = "no libmpv-2.dll in $mpvPath" }
        elseif ($mpvBuild -and $latestMpv -and ([regex]::Match($mpvBuild, '\d{8}').Value -lt [regex]::Match($latestMpv, '\d{8}').Value)) { $state = "NEWER"; $hint = $mpvPath }
        else { $state = "OK"; $hint = $mpvPath }
        Add-Result "libmpv Path ($configuration)" $(if ($mpvBuild) { $mpvBuild } else { "?" }) $latestMpv $state $hint
    }
}

# licenses (-Licenses) ----------------------------------------------------------------------------------------
# Licenses that can be combined with the GPL-3.0 of OpenTuner without a second look (a rough guide, not legal advice).
$knownLicenses = @("MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "LGPL-2.1-or-later", "LGPL-2.1-only",
    "LGPL-3.0-or-later", "LGPL-3.0-only", "GPL-3.0-or-later", "GPL-3.0-only")
$script:licenseCache = @{}

function Get-NuGetLicense([string]$Id, [string]$Version) {
    $key = "$Id|$Version"
    if ($script:licenseCache.ContainsKey($key)) { return $script:licenseCache[$key] }
    $result = "not stated"
    try {
        $lid = $Id.ToLower(); $lver = $Version.ToLower()
        $nuspec = [xml](Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$lid/$lver/$lid.nuspec" -UserAgent $userAgent -UseBasicParsing -TimeoutSec 30).Content
        $meta = $nuspec.package.metadata
        if ($meta.license -and $meta.license.type -eq "expression") { $result = [string]$meta.license.InnerText }
        elseif ($meta.license -and $meta.license.type -eq "file") { $result = "file: " + [string]$meta.license.InnerText + " (in the package)" }
        elseif ($meta.licenseUrl) { $result = "url: " + [string]$meta.licenseUrl }
    }
    catch { $result = "unknown (nuget.org: " + $_.Exception.Message + ")" }
    $script:licenseCache[$key] = $result
    return $result
}

if ($Licenses) {
    Write-Host "Licenses: reading the .nuspec of every NuGet package (this takes a moment) ..."
    foreach ($r in @($results | Where-Object { $_.Kind -eq "nuget" })) {
        $lic = Get-NuGetLicense $r.Key $r.Have
        $r.License = if ($knownLicenses -contains $lic) { $lic } else { "$lic (check)" }
    }
    foreach ($r in @($results | Where-Object { $_.Section -eq "External" -and $_.What -match "^(FFmpeg|libmpv)" })) {
        # the builds of the zip (THIRD-PARTY-NOTICES.md); not read from the files
        $r.License = if ($r.What -match "^FFmpeg") { "GPL-3.0-or-later (gyan full_build)" } else { "GPL-2.0-or-later (mpv build)" }
    }
}

# result ---------------------------------------------------------------------------------------------------
Write-Host ""
function Show-Table {
    $titles = [ordered]@{
        "External"    = "External: download and install by hand (the FFmpeg major version has to match the NuGet package FlyleafLib)"
        "NuGet"       = "NuGet: restored automatically, the version is set in opentuner.csproj"
        "Consistency" = "Consistency of the repo files (no dependency, only a comparison)"
        "Machine"     = "This machine: Playback Paths in the settings of bin\Debug and bin\Release (personal, not in the repo)"
    }
    # number the rows in the order they are shown, then size the columns to the longest entry
    $ordered = @()
    foreach ($section in $titles.Keys) { $ordered += @($results | Where-Object { $_.Section -eq $section }) }
    $n = 0
    foreach ($r in $ordered) { $n++; $r | Add-Member -NotePropertyName Number -NotePropertyValue $n -Force }
    $wWhat = [Math]::Max(4, (($ordered | ForEach-Object { $_.What.Length }) | Measure-Object -Maximum).Maximum)
    $wHave = [Math]::Max(14, (($ordered | ForEach-Object { $_.Have.Length }) | Measure-Object -Maximum).Maximum)
    $wLatest = [Math]::Max(6, (($ordered | ForEach-Object { $_.Latest.Length }) | Measure-Object -Maximum).Maximum)
    $wDoc = [Math]::Max(20, (($ordered | ForEach-Object { $_.Documented.Length }) | Measure-Object -Maximum).Maximum)
    $wLic = [Math]::Max(7, (($ordered | ForEach-Object { $_.License.Length }) | Measure-Object -Maximum).Maximum)
    if ($Licenses) {
        $format = "{0,3}  {1,-$wWhat}  {2,-$wHave}  {3,-$wLatest}  {4,-$wDoc}  {5,-$wLic}  {6,-8}  {7}"
        Write-Host ($format -f "#", "What", "Used / bundled", "Latest", "Documented as tested", "License", "State", "Hint")
    }
    else {
        $format = "{0,3}  {1,-$wWhat}  {2,-$wHave}  {3,-$wLatest}  {4,-$wDoc}  {5,-8}  {6}"
        Write-Host ($format -f "#", "What", "Used / bundled", "Latest", "Documented as tested", "State", "Hint")
    }
    foreach ($section in $titles.Keys) {
        $rows = @($ordered | Where-Object { $_.Section -eq $section })
        if ($rows.Count -eq 0) { continue }
        Write-Host ""
        Write-Host $titles[$section] -ForegroundColor Cyan
        foreach ($r in $rows) {
            $color = switch ($r.State) { "OK" { "Green" } "NEWER" { "Yellow" } "DIFFERS" { "Yellow" } "MISSING" { "Red" } default { "Gray" } }
            if ($Licenses) { Write-Host ($format -f $r.Number, $r.What, $r.Have, $r.Latest, $r.Documented, $r.License, $r.State, $r.Hint) -ForegroundColor $color }
            else { Write-Host ($format -f $r.Number, $r.What, $r.Have, $r.Latest, $r.Documented, $r.State, $r.Hint) -ForegroundColor $color }
        }
    }
}
Show-Table
Write-Host ""
$newer = @($results | Where-Object { $_.State -eq "NEWER" }).Count
$differs = @($results | Where-Object { $_.State -eq "DIFFERS" }).Count
if ($newer -gt 0) { Write-Host "$newer item(s) with a newer version: read the change list, test it, then update SETUP.md and the paths (see .DESCRIPTION)." -ForegroundColor Yellow }
if ($differs -gt 0) { Write-Host "$differs file(s) name another FFmpeg version than opentuner.csproj (what the zip will contain): make them agree." -ForegroundColor Yellow }
$missing = @($results | Where-Object { $_.State -eq "MISSING" }).Count
if ($missing -gt 0) { Write-Host "$missing configured path(s) on this machine do not contain the library." -ForegroundColor Red }
if ($newer -eq 0 -and $differs -eq 0 -and $missing -eq 0) { Write-Host "Nothing newer found, the repo files agree (UNKNOWN entries could not be checked)." -ForegroundColor Green }

if ($Licenses) {
    Write-Host ""
    Write-Host "Licenses of the NuGet packages (rough guide for the GPL-3.0 of OpenTuner, not legal advice):" -ForegroundColor Cyan
    $nugetRows = @($results | Where-Object { $_.Kind -eq "nuget" })
    foreach ($group in ($nugetRows | Group-Object { $_.License } | Sort-Object Count -Descending)) {
        $color = if ($group.Name -match "\(check\)") { "Yellow" } else { "Gray" }
        Write-Host ("  {0,-45} {1,2} package(s): {2}" -f $group.Name, $group.Count, (($group.Group | ForEach-Object { $_.What }) -join ", ")) -ForegroundColor $color
    }
    $checks = @($nugetRows | Where-Object { $_.License -match "\(check\)" }).Count
    if ($checks -gt 0) { Write-Host "$checks package(s) marked (check): read their license (file or URL)." -ForegroundColor Yellow }
}

# details (the delta) -------------------------------------------------------------------------------------
function Show-Details($r) {
    Write-Host ""
    Write-Host ("#{0} {1}: {2} -> {3}" -f $r.Number, $r.What, $r.Have, $r.Latest) -ForegroundColor Cyan
    $page = $null
    switch ($r.Kind) {
        "nuget" {
            $id = $r.Key.ToLower(); $latest = $r.Latest.ToLower()
            $page = "https://www.nuget.org/packages/$($r.Key)/$($r.Latest)#releasenotes-body-tab"
            try {
                $idx = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$id/index.json" -UserAgent $userAgent -TimeoutSec 30
                $versions = @($idx.versions)
                $from = [array]::IndexOf($versions, $r.Have.ToLower())
                $to = [array]::IndexOf($versions, $latest)
                if ($from -ge 0 -and $to -gt $from) {
                    $between = @($versions[($from + 1)..$to])
                    Write-Host ("  {0} release(s) since {1}: {2}" -f $between.Count, $r.Have, ($between -join ", "))
                }
                $nuspec = [xml](Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$id/$latest/$id.nuspec" -UserAgent $userAgent -UseBasicParsing -TimeoutSec 30).Content
                $meta = $nuspec.package.metadata
                $repo = $meta.repository.url -replace '\.git$', ''
                if ($meta.projectUrl) { Write-Host "  Project:   $($meta.projectUrl)" }
                if ($repo) { Write-Host "  Repo:      $repo" }
                if ($repo -match '^https://github\.com/') { Write-Host "  Releases:  $repo/releases"; $page = "$repo/releases" }
                if ($meta.releaseNotes) {
                    $notes = ($meta.releaseNotes -replace '\s+', ' ').Trim()
                    if ($notes.Length -gt 700) { $notes = $notes.Substring(0, 700) + " ..." }
                    Write-Host "  Release notes of ${latest}: $notes"
                }
                else { Write-Host "  (the package has no release notes text, look at the releases page / repo)" }
            }
            catch { Write-Warning "Could not read the NuGet data: $($_.Exception.Message)" }
            Write-Host "  NuGet:     https://www.nuget.org/packages/$($r.Key)/$($r.Latest)"
        }
        "ffmpeg" {
            $page = "https://github.com/FFmpeg/FFmpeg/compare/n$($r.Have)...n$($r.Latest)"
            Write-Host "  Changelog: https://github.com/FFmpeg/FFmpeg/blob/n$($r.Latest)/Changelog"
            Write-Host "  Compare:   $page"
            Write-Host "  Build:     https://github.com/GyanD/codexffmpeg/releases/tag/$($r.Latest)"
        }
        "mpv" {
            $page = "https://github.com/mpv-player/mpv/releases"
            Write-Host "  The shinchiro builds have no change list of their own, mpv's releases: $page"
            Write-Host "  Download:  https://sourceforge.net/projects/mpv-player-windows/files/libmpv/"
        }
        default { Write-Host "  $($r.Hint)" }
    }
    if ($Open -and $page) { Start-Process $page }
}

function Get-Numbers([string]$Text) {
    return @($Text -split '[,\s]+' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
}
function Show-Rows([int[]]$Numbers) {
    foreach ($number in $Numbers) {
        $row = $results | Where-Object { $_.Number -eq $number } | Select-Object -First 1
        if ($row) { Show-Details $row } else { Write-Warning "No row #$number" }
    }
}

if ($Details) {
    Show-Rows (Get-Numbers $Details)
}
elseif (-not [Console]::IsInputRedirected -and [Environment]::UserInteractive) {
    # asks again and again until it is ended explicitly
    while ($true) {
        Write-Host ""
        $answer = (Read-Host "Changes for which numbers? (e.g. 3,19; l = list again; q = quit)").Trim().ToLower()
        if ($answer -in @("q", "quit", "e", "end", "ende", "exit")) { break }
        if ($answer -eq "l") { Show-Table; continue }
        $numbers = Get-Numbers $answer
        if ($numbers.Count -eq 0) { Write-Host "Please enter row numbers, l or q." -ForegroundColor Gray; continue }
        Show-Rows $numbers
    }
}
exit 0
