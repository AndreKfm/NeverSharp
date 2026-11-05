set -e
cd /d/Private/Source/Andre/projects/NeverSharp
S="C:/Users/akn/AppData/Local/Temp/claude/3c25817852539760/fdf5d608-b70b-4fbe-99c0-1f5238dfd2f7/scratchpad"
export WarningsNotAsErrors=NU1903
node harness/bench/build-cs.mjs --tracks optimized --runtimes aot-ws
git status --short
rm -rf "$S/RS1m/fin" && mkdir -p "$S/RS1m/fin" && cp -r artifacts/bin/cs/optimized-aot-ws "$S/RS1m/fin/"
F="$S/RS1m/fin/optimized-aot-ws/parse-cs-optimized-aot-ws.exe"; B="$S/RS1/base/optimized-aot-ws/parse-cs-optimized-aot-ws.exe"
IL=results/phase4/EO1/run1/follow-up/interleave.mjs
TE=artifacts/bench/filelists/tuning-check-emit.txt
TC=artifacts/bench/filelists/tuning-check.txt
node $IL "$S/RS1m/il-emit.json" 20 2 $TE --check --emit -- "base=$B" "final=$F"
node $IL "$S/RS1m/il-emit-single.json" 20 2 $TE --check --emit --single -- "base=$B" "final=$F"
node $IL "$S/RS1m/il-check.json" 20 2 $TC --check -- "base=$B" "final=$F"
node $IL "$S/RS1m/il-check12.json" 20 2 $TC --check --checkers 12 -- "base=$B" "final=$F"
node $IL "$S/RS1m/il-check-single.json" 20 2 $TC --check --single -- "base=$B" "final=$F"
echo ALLDONE
