$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$versionPath = Join-Path $root "version.json"
$buildPath = Join-Path $root "build.ps1"
$sourcePath = Join-Path $root "src\WindowLayoutLauncher.cs"
$releaseWorkflowPath = Join-Path $root ".github\workflows\release.yml"

foreach ($required in @($versionPath, $buildPath, $sourcePath, $releaseWorkflowPath)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required release file is missing: $required"
    }
}

$metadata = Get-Content -LiteralPath $versionPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not ($metadata.version -match '^\d+\.\d+\.\d+$')) {
    throw "version.json must contain a semantic version: $($metadata.version)"
}

$expectedReleaseSegment = "/releases/download/v$($metadata.version)/"
if (-not ([string]$metadata.download_url).Contains($expectedReleaseSegment)) {
    throw "download_url must point to the matching v$($metadata.version) release"
}
if (-not ([string]$metadata.download_url).StartsWith("https://github.com/zwmopen/WindowLayoutLauncher/")) {
    throw "download_url must use the canonical repository"
}

$build = Get-Content -LiteralPath $buildPath -Raw -Encoding UTF8
if ($build -match 'Set-Content\s+-LiteralPath\s+\$src\b') {
    throw "Build script must never overwrite tracked source"
}
if (-not $build.Contains("WindowLayoutLauncher.generated.")) {
    throw "Build script must compile from an isolated generated source file"
}
if (-not $build.Contains("Remove-Item -LiteralPath `$generatedSrc")) {
    throw "Build script must clean generated source in finally"
}

$workflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw -Encoding UTF8
foreach ($requiredText in @("Validate tag and version metadata", "SHA256SUMS.txt", "gh release create")) {
    if (-not $workflow.Contains($requiredText)) {
        throw "Release workflow is missing required contract: $requiredText"
    }
}

Write-Output "Release contract OK: version $($metadata.version)"
