#!/bin/sh
S="C:/Users/akn/AppData/Local/Temp/claude/3c25817852539760/fdf5d608-b70b-4fbe-99c0-1f5238dfd2f7/scratchpad"
L=D:/Private/Source/Andre/projects/NeverSharp/artifacts/bench/filelists
cd "$S/MM1m"
for r in bluesky playwright tldraw sentry vscode; do
  node "$S/SC1/inter.mjs" "il-$r.json" 5 1 "$L/$r.txt" variants.json > "log-$r.txt" 2>&1 || echo "FAIL $r"
done
echo done
