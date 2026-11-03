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

$dirs = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319"
)

$compiler = $null
foreach ($d in $dirs) {
    $c = Join-Path $d "csc.exe"
    if (Test-Path -LiteralPath $c) { $compiler = $c; break }
}
if (-not $compiler) {
    $found = Get-ChildItem -Path "$env:WINDIR\Microsoft.NET" -Filter "csc.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $compiler = $found.FullName }
}
if (-not $compiler) { throw "Cannot find .NET Framework C# compiler." }

function Resolve-Assembly($name) {
    $file = $name + ".dll"
    $framework = Join-Path "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319" $file
    if (Test-Path -LiteralPath $framework) { return $framework }

    $roots = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\WPF",
        "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL",
        "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework"
    )
    foreach ($r in $roots) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        $hit = Get-ChildItem -Path $r -Filter $file -Recurse -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -notmatch "v3\.5|\\amd64|\\x86|\\ia64" } |
               Sort-Object { $_.FullName.Length } |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

$outputDirectory = Split-Path -Parent $out
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

if (-not (Test-Path -LiteralPath $src)) {
    throw "Cannot find source file: $src"
}
if (-not (Test-Path -LiteralPath $icon)) {
    throw "Cannot find app icon: $icon"
}

$refs = @("System.Windows.Forms", "System.Drawing", "System.Runtime.Serialization", "System.Management", "UIAutomationClient", "UIAutomationTypes", "Microsoft.CSharp")
$refArgs = @()
foreach ($r in $refs) {
    $p = Resolve-Assembly $r
    if (-not $p) { throw "Cannot locate assembly: $r" }
    $refArgs += ("/reference:" + $p)
}

& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:"$icon" /out:"$out" $refArgs "$src"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

Write-Output "Built=$out"
