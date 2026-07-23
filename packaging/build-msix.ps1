<#
.SYNOPSIS
    Turn the staged package layout into a signed-ready .msixbundle. Run on Windows 11.

.DESCRIPTION
    scripts/stage-msix.py (run on macOS) produces artifacts/msix-b<N>/{x64,arm64}.
    This script copies that layout to a local working folder, builds resources.pri
    for each architecture, packs each into a .msix, and bundles the two together.

    The copy is not optional: Parallels shared folders can serve stale bytes, and
    packaging tools read every file in the tree.

    Keep this file ASCII-only. Windows PowerShell 5.1 reads a BOM-less script as
    the system ANSI code page (CP932 on a Japanese system), so any multi-byte
    character turns into mojibake and breaks parsing.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-msix.ps1 -Source W:\01_Active\PdfImageRemoverForRag\artifacts\msix-b28
#>

[CmdletBinding()]
param(
    # The artifacts/msix-b<N> folder on the shared drive.
    [Parameter(Mandatory = $true)]
    [string] $Source,

    # Local working copy. Anything already here is replaced.
    [string] $Work = "C:\work\msix",

    # Windows SDK binaries. arm64 is native on this VM; x64 also works.
    [string] $Sdk = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\arm64",

    # Languages declared in the manifest's <Resources> block.
    [string] $Languages = "en-US_ja-JP"
)

$ErrorActionPreference = "Stop"

function Invoke-Tool {
    param([string] $Exe, [string[]] $Arguments)
    # Tools report failure through the exit code, not through exceptions, so
    # every call is checked explicitly.
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path $Exe -Leaf) failed with exit code $LASTEXITCODE"
    }
}

$makepri  = Join-Path $Sdk "makepri.exe"
$makeappx = Join-Path $Sdk "makeappx.exe"
foreach ($tool in $makepri, $makeappx) {
    if (-not (Test-Path $tool)) { throw "not found: $tool  (check -Sdk)" }
}
if (-not (Test-Path $Source)) { throw "not found: $Source" }

# --- 1. Local copy -------------------------------------------------------
Write-Host "copying $Source -> $Work" -ForegroundColor Cyan
if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
Copy-Item $Source $Work -Recurse

$bundleDir = Join-Path $Work "bundle"
New-Item $bundleDir -ItemType Directory -Force | Out-Null

# The config must live outside the package folders, or it ends up inside the
# .msix as a stray file.
$priConfig = Join-Path $Work "priconfig.xml"
Invoke-Tool $makepri @("createconfig", "/cf", $priConfig, "/dq", $Languages, "/o")

# --- 2. One .msix per architecture ---------------------------------------
foreach ($arch in "x64", "arm64") {
    $stage = Join-Path $Work $arch
    if (-not (Test-Path (Join-Path $stage "AppxManifest.xml"))) {
        throw "missing AppxManifest.xml in $stage"
    }

    Write-Host "building resources.pri for $arch" -ForegroundColor Cyan
    Invoke-Tool $makepri @("new", "/pr", $stage, "/cf", $priConfig,
                           "/of", (Join-Path $stage "resources.pri"), "/o")

    Write-Host "packing $arch" -ForegroundColor Cyan
    Invoke-Tool $makeappx @("pack", "/d", $stage,
                            "/p", (Join-Path $bundleDir "PdfImageRemoverForRag-$arch.msix"), "/o")
}

# --- 3. Bundle both ------------------------------------------------------
# The bundle folder must contain nothing but the .msix files.
$bundle = Join-Path $Work "PdfImageRemoverForRag.msixbundle"
Write-Host "bundling" -ForegroundColor Cyan
Invoke-Tool $makeappx @("bundle", "/d", $bundleDir, "/p", $bundle, "/o")

$sizeMb = [math]::Round((Get-Item $bundle).Length / 1MB, 1)
Write-Host ""
Write-Host "done: $bundle  ($sizeMb MB)" -ForegroundColor Green
Write-Host "Upload this file to Partner Center as-is - it does not need signing." -ForegroundColor Green
