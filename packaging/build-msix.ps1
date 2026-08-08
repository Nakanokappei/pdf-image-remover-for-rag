<#
.SYNOPSIS
    Turn the staged package layout into a signed-ready .msixbundle. Run on Windows 11.

.DESCRIPTION
    scripts/stage-msix.py produces artifacts/msix-b<N>/{x64,arm64}. This script
    copies that layout to a working folder, builds resources.pri for each
    architecture, packs each into a .msix, and bundles the two together.

    The copy is not optional: packaging tools read and rewrite every file in the
    tree, and the staged layout has to survive a failed run unchanged.

    The working folder defaults to <Source>-work, beside the source under
    artifacts/. Everything this repository produces stays under artifacts/ (user's
    decision, 2026-08-08); nothing is written to C:\work any more. The build
    number in the source path gives every run a folder of its own, which is what
    the stale-bytes rule actually asks for - a re-used path is the problem, not
    the shared volume.

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

    # Working copy. Defaults to <Source>-work. Anything already here is replaced.
    [string] $Work = "",

    # Windows SDK binaries. arm64 is native on this VM; x64 also works.
    [string] $Sdk = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\arm64",

    # Languages declared in the manifest's <Resources> block.
    [string] $Languages = "en-US_ja-JP_de-DE_fr-FR_es-ES_it-IT_pt-BR_ru-RU_ko-KR_zh-CN_zh-TW_id-ID_ms-MY_hi-IN_tr-TR_vi-VN"
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

# Derived rather than defaulted in the param block, so it follows the build number
# in the source path and no two runs share a folder.
if ([string]::IsNullOrEmpty($Work)) {
    $Work = (Join-Path (Split-Path $Source -Parent) ((Split-Path $Source -Leaf) + "-work"))
}

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
