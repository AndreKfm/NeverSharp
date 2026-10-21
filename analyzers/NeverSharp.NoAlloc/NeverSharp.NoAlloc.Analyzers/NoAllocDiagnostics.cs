// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0

using Microsoft.CodeAnalysis;

namespace NeverSharp.NoAlloc.Analyzers;

/// <summary>NS001–NS015 as README.md specifies them. NS001–NS007, NS011 and NS012 prove an operation; NS008 and NS013 are contract breaches.</summary>
internal static class NoAllocDiagnostics
{
    private const string Category = "NoAlloc";

    public static readonly DiagnosticDescriptor Allocation = Rule(
        "NS001", "Allocation in a NoAlloc section", "'{0}' allocates on the managed heap: {1}");

    public static readonly DiagnosticDescriptor BoxingOrCapture = Rule(
        "NS002", "Boxing or closure capture in a NoAlloc section", "'{0}' allocates: {1}");

    public static readonly DiagnosticDescriptor Lock = Rule(
        "NS003", "Lock, spin or wait in a NoLock section", "'{0}' locks or spins: {1}");

    public static readonly DiagnosticDescriptor Blocking = Rule(
        "NS004", "Blocking call in a NoBlock section", "'{0}' blocks: {1}");

    public static readonly DiagnosticDescriptor Async = Rule(
        "NS005", "Async in a NoAsync section", "'{0}' is async");

    public static readonly DiagnosticDescriptor Throw = Rule(
        "NS006", "Explicit throw in a NoThrow section", "'{0}' throws explicitly");

    public static readonly DiagnosticDescriptor Dynamic = Rule(
        "NS007", "Reflection or dynamic in a NoDynamic section", "'{0}' uses reflection or dynamic binding: {1}");

    public static readonly DiagnosticDescriptor NoContract = Rule(
        "NS008", "Callee without contract", "'{0}' calls '{1}', which has no verified or assumed contract for {2}");

    public static readonly DiagnosticDescriptor MissingReason = Rule(
        "NS009", "Relaxation without reason", "{0} on '{1}' needs a non-empty reason");

    public static readonly DiagnosticDescriptor RelaxedOverride = Rule(
        "NS010", "Override relaxes an inherited marker", "'{0}' does not keep {1} of the contract it inherits from '{2}'");

    public static readonly DiagnosticDescriptor TypeInitialization = Rule(
        "NS011", "Possible type initialization in a NoInit section", "'{0}' may run the static constructor of '{1}'");

    public static readonly DiagnosticDescriptor PoolRent = Rule(
        "NS012", "Pool rent in a NoAlloc section", "'{0}' rents from a pool: '{1}'");

    public static readonly DiagnosticDescriptor RelaxedCallee = Rule(
        "NS013", "Callee relaxed (contract breach)", "'{0}' calls '{1}', which does not promise {2}: it is relaxed by {3}");

    public static readonly DiagnosticDescriptor UnresolvedAllowlistEntry = Rule(
        "NS014", "Allowlist entry does not resolve", "Allowlist entry '{0}' does not resolve to any symbol in this compilation",
        DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor MalformedAllowlistEntry = Rule(
        "NS015", "Allowlist entry is malformed", "Allowlist entry is malformed: {0}",
        DiagnosticSeverity.Warning);

    /// <summary>The constraint a proven operation violates.</summary>
    public static Constraint ConstraintOf(DiagnosticDescriptor descriptor) => descriptor.Id switch
    {
        "NS001" or "NS002" or "NS012" => Constraint.NoAlloc,
        "NS003" => Constraint.NoLock,
        "NS004" => Constraint.NoBlock,
        "NS005" => Constraint.NoAsync,
        "NS006" => Constraint.NoThrow,
        "NS007" => Constraint.NoDynamic,
        "NS011" => Constraint.NoInit,
        _ => Constraint.None,
    };

    private static DiagnosticDescriptor Rule(
        string id, string title, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new(id, title, message, Category, severity, isEnabledByDefault: true);
}
