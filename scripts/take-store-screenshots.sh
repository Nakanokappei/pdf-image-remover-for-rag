#!/usr/bin/env bash
# Photograph the app for the store listing: every view, in every language.
#
#   scripts/take-store-screenshots.sh artifacts/win-arm64-b127
#
# The app holds the poses and writes the files (see --list-views); this only
# walks the matrix. Japanese uses the Japanese demo documents and every other
# language the English ones — the document is the subject, not the translation.
set -eu

app="${1:?usage: take-store-screenshots.sh <published-folder> [view ...]}"
exe="$app/PdfImageRemoverForRag.exe"
shift || true

languages=$("$exe" --list-views | awk '/^languages:/{getline; print $0}')
views="${*:-$("$exe" --list-views | awk '/^views:/{f=1;next} /^languages:/{f=0} f{print $1}')}"

for language in $languages; do
  case "$language" in
    ja) documents=(demo-pdfs/ja/*.pdf) ;;
    *)  documents=(demo-pdfs/en/*.pdf) ;;
  esac

  for view in $views; do
    out="docs/images/store-$view-$language.png"
    echo "$out"
    "$exe" "${documents[@]}" --language "$language" --screenshot "view=$view" "out=$out"
  done
done
