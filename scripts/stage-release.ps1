<#
.SYNOPSIS
    Stage and zip the GitHub Releases artifacts for both architectures.

.DESCRIPTION
    Reads the dotnet publish output for x64 and arm64, drops the debug symbols,
    adds the license files that have to travel with the binaries, and writes one
    zip per architecture.

    The zip entries are built by hand. Compress-Archive and
    ZipFile.CreateFromDirectory put a backslash between the directory and the
    file name inside each entry; macOS and Linux do not treat that as a path
    separator, so the archive unpacks as ~490 flat files with backslashes in
    their names. This script writes forward slashes and then asserts that no
    entry contains a backslash, because that failure is invisible on Windows -
    Explorer and Expand-Archive both unpack such an archive correctly.

    Run dotnet publish first, into artifacts/win-<rid>-b<Build>. This script
    only stages what is already there, the same split stage-msix.py uses.

    Keep this file ASCII-only. Windows PowerShell 5.1 reads a BOM-less script as
    the system ANSI code page (CP932 on a Japanese system), so any multi-byte
    character turns into mojibake and breaks parsing.

.EXAMPLE
    .\scripts\stage-release.ps1 -Version 1.1.0 -Build 77
#>

[CmdletBinding()]
param(
    # Product version, as it appears in the App csproj and the git tag.
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    # Build number, naming the artifacts/win-<rid>-b<N> folders to read.
    [Parameter(Mandatory = $true)]
    [int] $Build,

    # Where the staged folders and zips are written. Replaced if it exists.
    [string] $Output = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = Split-Path $PSScriptRoot -Parent
if ($Output -eq "") { $Output = "C:\work\release-$Version" }

# Written as character codes so no literal backslash is needed in a string that
# is itself about backslashes.
$backslash = [char]92
$forward = [char]47

New-Item -ItemType Directory $Output -Force | Out-Null

foreach ($arch in "x64", "arm64") {
    $published = Join-Path $repo "artifacts\win-$arch-b$Build"
    if (-not (Test-Path $published)) {
        throw "not found: $published  (run dotnet publish for win-$arch first)"
    }

    # The published binary is the authority on what is being shipped. A mismatch
    # here means the folder holds an older build than the one being released,
    # which is exactly what the numbered-folder rule exists to prevent.
    $exe = Join-Path $published "PdfImageRemoverForRag.exe"
    if (-not (Test-Path $exe)) { throw "not found: $exe" }
    $productVersion = (Get-Item $exe).VersionInfo.ProductVersion
    $expected = "$Version+$Build"
    if ($productVersion -ne $expected) {
        throw "$arch : the published exe says $productVersion, expected $expected"
    }

    $name = "PdfImageRemoverForRag-$Version-win-$arch"
    $stage = Join-Path $Output $name
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
    New-Item -ItemType Directory $stage -Force | Out-Null

    # Everything except the debug symbols: .NET runs fine without them and they
    # roughly double the download.
    $publishedRoot = (Resolve-Path $published).Path
    Get-ChildItem $published -Recurse -File |
        Where-Object { $_.Extension -ne ".pdb" } |
        ForEach-Object {
            $relative = $_.FullName.Substring($publishedRoot.Length + 1)
            $target = Join-Path $stage $relative
            New-Item -ItemType Directory (Split-Path $target) -Force | Out-Null
            Copy-Item $_.FullName $target
        }

    # The MIT and Apache notices must ship with the distribution.
    Copy-Item (Join-Path $repo "LICENSE") (Join-Path $stage "LICENSE.txt")
    Copy-Item (Join-Path $repo "docs\license-notices.md") (Join-Path $stage "LICENSE-NOTICES.md")

    $zipPath = Join-Path $Output "$name.zip"
    if (Test-Path $zipPath) { [System.IO.File]::Delete($zipPath) }

    $stream = [System.IO.File]::Create($zipPath)
    try {
        $archive = New-Object System.IO.Compression.ZipArchive(
            $stream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $stageRoot = (Resolve-Path $stage).Path
            Get-ChildItem $stage -Recurse -File | ForEach-Object {
                $relative = $_.FullName.Substring($stageRoot.Length + 1).Replace($backslash, $forward)
                $entry = $archive.CreateEntry(
                    "$name$forward$relative",
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entryStream = $entry.Open()
                try {
                    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally { $entryStream.Close() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Close() }

    # Read the archive back and prove the separators survived. Checking the
    # strings that were written would only prove this script's own arithmetic.
    $written = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $bad = @($written.Entries | Where-Object { $_.FullName.Contains($backslash) })
        if ($bad.Count -gt 0) {
            throw "$name.zip : $($bad.Count) entries contain a backslash, e.g. $($bad[0].FullName)"
        }
        $sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
        Write-Host ("{0}.zip  {1} MB  {2} entries, none with a backslash" -f `
            $name, $sizeMb, $written.Entries.Count) -ForegroundColor Green
    }
    finally { $written.Dispose() }
}

Write-Host ""
Write-Host "staged in $Output" -ForegroundColor Green
Write-Host "next: gh release create v$Version <zips> --title ... --notes ...   (English)"
