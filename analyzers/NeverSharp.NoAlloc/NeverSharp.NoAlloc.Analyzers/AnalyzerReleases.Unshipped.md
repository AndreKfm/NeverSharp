; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NS001 | NoAlloc | Error | Allocation
NS002 | NoAlloc | Error | Boxing or closure capture
NS003 | NoAlloc | Error | Lock, spin or wait
NS004 | NoAlloc | Error | Blocking call
NS005 | NoAlloc | Error | Async
NS006 | NoAlloc | Error | Explicit throw
NS007 | NoAlloc | Error | Reflection or dynamic
NS008 | NoAlloc | Error | Callee without contract
NS009 | NoAlloc | Error | Relaxation without reason
NS010 | NoAlloc | Error | Override relaxes an inherited marker
NS011 | NoAlloc | Error | Possible type initialization
NS012 | NoAlloc | Error | Pool rent
NS013 | NoAlloc | Error | Callee relaxed (contract breach)
NS014 | NoAlloc | Warning | Allowlist entry does not resolve
NS015 | NoAlloc | Warning | Allowlist entry is malformed
