#!/usr/bin/env bash
# Baut die EXE und veröffentlicht sie als GitHub-Release (Version aus der .csproj).
# Aufruf: tools/release.sh "Was ist neu …"
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(grep -oP '(?<=<Version>)[^<]+' src/BankManager.Gui/BankManager.Gui.csproj)
OUT=artifacts/release
rm -rf "$OUT" && mkdir -p "$OUT"
dotnet test
dotnet publish src/BankManager.Gui -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -o "$OUT/publish"
cp "$OUT/publish/BankManager.exe" "$OUT/"
(cd "$OUT" && sha256sum BankManager.exe > BankManager.exe.sha256 && zip -q -9 BankManager.zip BankManager.exe)
gh release create "v$VERSION" --title "Bank-Manager $VERSION" --notes "${1:-}" \
  "$OUT/BankManager.exe" "$OUT/BankManager.exe.sha256" "$OUT/BankManager.zip"
