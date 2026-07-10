param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src\WindowLayoutLauncher.cs"
$icon = Join-Path $root "assets\app.ico"
$defaultRelease = Join-Path $root "release"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $out = Join-Path $defaultRelease "窗口布局启动器.exe"
} elseif ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $out = [System.IO.Path]::GetFullPath($OutputPath)
} else {
    $out = [System.IO.Path]::GetFullPath((Join-Path $root $OutputPath))
}

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $csc)) {
    $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path -LiteralPath $csc)) {
    throw "Cannot find .NET Framework C# compiler."
}

if (-not (Test-Path -LiteralPath $src)) {
    throw "Cannot find source file: $src"
}

if (-not (Test-Path -LiteralPath $icon)) {
    throw "Cannot find app icon: $icon"
}

$outputDirectory = Split-Path -Parent $out
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

# Older source snapshots may still contain a placeholder repository address.
# Normalize those values in a temporary generated file so a build never edits
# the tracked source tree.
$generatedSrc = Join-Path ([System.IO.Path]::GetTempPath()) (
    "WindowLayoutLauncher.generated.{0}.cs" -f ([Guid]::NewGuid().ToString("N"))
)

try {
    $sourceText = Get-Content -LiteralPath $src -Raw -Encoding UTF8
    $sourceText = $sourceText.Replace(
        "https://github.com/yourusername/WindowLayoutLauncher",
        "https://github.com/zwmopen/WindowLayoutLauncher"
    )
    $sourceText = $sourceText.Replace(
        "https://raw.githubusercontent.com/yourusername/WindowLayoutLauncher/main/version.json",
        "https://raw.githubusercontent.com/zwmopen/WindowLayoutLauncher/main/version.json"
    )
    $sourceText = $sourceText.Replace(
        "https://github.com/zwmopen/skills/tree/main/tools/window-layout-launcher",
        "https://github.com/zwmopen/WindowLayoutLauncher"
    )
    $sourceText = $sourceText.Replace(
        "https://raw.githubusercontent.com/zwmopen/skills/main/tools/window-layout-launcher/version.json",
        "https://raw.githubusercontent.com/zwmopen/WindowLayoutLauncher/main/version.json"
    )
    Set-Content -LiteralPath $generatedSrc -Value $sourceText -Encoding UTF8

    & $csc /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:$icon /out:$out `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Runtime.Serialization.dll `
        /reference:Microsoft.CSharp.dll `
        $generatedSrc

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
}
finally {
    Remove-Item -LiteralPath $generatedSrc -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $out)) {
    throw "Build completed without producing: $out"
}

Write-Output "Built=$out"
