set -e
cd /d/Private/Source/Andre/projects/NeverSharp
export WarningsNotAsErrors=NU1903
C=$(git rev-parse --short HEAD)
node harness/bench/build-cs.mjs --tracks optimized --runtimes aot-ws
git status --short
node harness/bench/bench.mjs --runs 5 --warmup 1 --mem-runs 1 --configs cs-o-aot-ws-check,tsgo7-check,tsrs-check,cs-o-aot-ws-check12,tsgo7-check12,cs-o-aot-ws-emit,tsgo7-emit,tsrs-emit --label "quick estimate after EO1, EO2, RS1 ($C), 5 runs"
ls -d artifacts/bench/results/*/ | tail -1
echo ALLDONE
