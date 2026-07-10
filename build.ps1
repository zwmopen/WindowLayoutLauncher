$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src\WindowLayoutLauncher.cs"
$icon = Join-Path $root "assets\app.ico"
$release = Join-Path $root "release"
$out = Join-Path $release "窗口布局启动器.exe"
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

# Normalize repository links before compiling. Older source snapshots used a
# placeholder owner or the former duplicate path in zwmopen/skills.
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
Set-Content -LiteralPath $src -Value $sourceText -Encoding UTF8

New-Item -ItemType Directory -Force -Path $release | Out-Null

if (-not (Test-Path -LiteralPath $icon)) {
    throw "Cannot find app icon: $icon"
}

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:$icon /out:$out `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.Runtime.Serialization.dll `
    /reference:Microsoft.CSharp.dll `
    $src

if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

Write-Output "Built=$out"
