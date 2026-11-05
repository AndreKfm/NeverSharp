const S=process.argv[2];const B=S+"/AR2/base/optimized-aot-",F=S+"/AR2/b5/optimized-aot-";
const ex=(p,g)=>p+g+"/parse-cs-optimized-aot-"+g+".exe";const R="D:/Private/Source/Andre/projects/NeverSharp/artifacts/bin/";
const v=[{name:"base-srv-tsrs",exe:ex(B,"srv"),flags:["--check","--pool","tsrs"]},{name:"final-srv-tsrs",exe:ex(F,"srv"),flags:["--check","--pool","tsrs"]},
{name:"base-ws-tsrs",exe:ex(B,"ws"),flags:["--check","--pool","tsrs"]},{name:"final-ws-tsrs",exe:ex(F,"ws"),flags:["--check","--pool","tsrs"]},
{name:"final-ws-default",exe:ex(F,"ws"),flags:["--check"]},{name:"tsrs",exe:R+"parse-rs.exe",flags:["--check"]},{name:"tsgo",exe:R+"parse-go.exe",flags:["--check"]}];
require("fs").writeFileSync(S+"/AR2m/variants.json",JSON.stringify(v,null,1));
for(const x of v) if(!require("fs").existsSync(x.exe)) console.log("MISSING",x.exe);
console.log(v.length);
