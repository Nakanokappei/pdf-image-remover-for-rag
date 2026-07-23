#!/usr/bin/env python3
"""Lay out the MSIX package contents for each architecture.

Run from the repository root, after publishing both architectures:

    python3 scripts/stage-msix.py 26

Reads  artifacts/win-x64-b<N>/ and artifacts/win-arm64-b<N>/   (dotnet publish output)
       assets/store/                                           (generated icon assets)
       packaging/Package.appxmanifest                           (template, {ARCH} placeholder)
Writes artifacts/msix-b<N>/<arch>/                              (one folder per architecture)

This is everything that can be done on macOS. Turning these folders into .msix
files needs MakePri.exe and MakeAppx.exe from the Windows SDK — see
docs/msix-packaging.md for the Windows-side commands.
"""

import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFEST_TEMPLATE = ROOT / "packaging" / "Package.appxmanifest"
STORE_ASSETS = ROOT / "assets" / "store"

# Publish RID -> the ProcessorArchitecture value the manifest expects.
ARCHITECTURES = {"win-x64": "x64", "win-arm64": "arm64"}


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: stage-msix.py <build-number>")
    build = sys.argv[1]

    staging_root = ROOT / f"artifacts/msix-b{build}"
    if staging_root.exists():
        shutil.rmtree(staging_root)

    template = MANIFEST_TEMPLATE.read_text(encoding="utf-8")

    for rid, arch in ARCHITECTURES.items():
        published = ROOT / f"artifacts/{rid}-b{build}"
        if not published.is_dir():
            raise SystemExit(f"missing publish output: {published.relative_to(ROOT)}")

        target = staging_root / arch
        # The published payload sits at the package root — that is where the
        # manifest's Executable attribute resolves from. Debug symbols are
        # excluded: the Windows App Certification Kit flags .pdb files in a Store
        # package, and they are dead weight at runtime (.NET runs fine without
        # them). They stay in the plain artifacts/win-*/ output for debugging.
        shutil.copytree(published, target, ignore=shutil.ignore_patterns("*.pdb"))

        # Icon assets go under Assets\, matching the manifest's Logo paths.
        shutil.copytree(STORE_ASSETS, target / "Assets")

        # The manifest is named AppxManifest.xml inside a package (the
        # Package.appxmanifest name only applies to the source file).
        (target / "AppxManifest.xml").write_text(
            template.replace("{ARCH}", arch), encoding="utf-8")

        size_mb = sum(f.stat().st_size for f in target.rglob("*") if f.is_file()) / 1e6
        print(f"{arch:>6}: {target.relative_to(ROOT)}  ({size_mb:.0f} MB)")


if __name__ == "__main__":
    main()
