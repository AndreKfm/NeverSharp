#!/bin/sh
# usage: build.sh <outdir> <extra msbuild props...>   builds aot-ws and aot-srv from the working tree
export WarningsNotAsErrors=NU1903
export PATH="/c/Program Files (x86)/Microsoft Visual Studio/Installer:$PATH"
P=D:/Private/Source/Andre/projects/NeverSharp/tools/NeverSharp.ParseDriver
O="$1"; shift
for rt in aot-ws aot-srv; do
  out=$(dotnet publish "$P" -c Release -r win-x64 --self-contained -p:NeverSharpTrack=Optimized -p:NeverSharpRuntime=$rt "$@" -o "$O/optimized-$rt" -nologo -v q 2>&1)
  if [ $? -ne 0 ] || echo "$out" | grep -q " error "; then echo "$out" | grep -E " error |warning" | sort -u | head -20; echo "BUILD FAILED $rt"; exit 1; fi
done
echo BUILD-OK $O
