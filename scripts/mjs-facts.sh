#!/usr/bin/env bash
# Refreshes the committed mermaid.js facts (tests/Mermaider.Tests/Snapshots/LayoutSkeleton/Reference/*.mjs.facts.txt)
# with the mermaid version package-lock.json pins. To move to another mermaid version, change it in package.json, run
# `npm install`, then run this. Arguments are passed to `dotnet run`, such as -c Release.
set -euo pipefail
cd "$(dirname "$0")/.."

npm ci --no-audit --no-fund

corpus=.artifacts/mjs-corpus
MJS_CORPUS_OUT="$PWD/$corpus" dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj "$@" -- \
  --treenode-filter "/*/*/MjsFactsTests/Export_corpus"

# The SVGs land where MjsComparisonTests reads its mermaid.js references (gitignored).
node scripts/mjs-facts.mjs "$corpus" tests/Mermaider.Tests/Snapshots/LayoutSkeleton/Reference \
  tests/Mermaider.Tests/Snapshots/Reference/mermaidjs
