// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0

using static NeverSharp.NoAlloc.Analyzers.Tests.Snippets;

namespace NeverSharp.NoAlloc.Analyzers.Tests;

/// <summary>Delegates, local functions, async, throw, lock, blocking, reflection and type initialization.</summary>
public sealed class ScopeTests
{
    [Fact]
    public Task Lambdas_static_lambdas_and_method_groups_create_delegates() =>
        Verifier.VerifyAsync(InHotMethod(
            "Func<int> f = {|NS001:() => 1|}; Func<int> g = {|NS001:static () => 2|}; Func<int> h = {|NS001:Get|};",
            "static int Get() => 1;"));

    [Fact]
    public Task Capturing_lambdas_allocate_a_closure() =>
        Verifier.VerifyAsync(InHotMethod(
            "Func<int> f = {|NS002:() => i|}; Func<int> g = {|NS002:() => Field|};",
            "int Field;"));

    [Fact]
    public Task Lambda_body_is_not_analyzed_separately() =>
        Verifier.VerifyAsync(InHotMethod("Action f = {|NS001:() => { var x = new object(); }|};"));

    [Fact]
    public Task Delegate_and_function_pointer_invocations_have_no_contract() =>
        Verifier.VerifyAsync(InHotMethod(
            "var x = {|NS008:F()|}; delegate*<int> p = &Get; var y = {|NS008:p()|};",
            "Func<int> F = null!; static int Get() => 1;"));

    [Fact]
    public Task Local_function_in_marked_method_inherits_the_contract() =>
        Verifier.VerifyAsync(InHotMethod("void L() { var x = {|NS001:new object()|}; } L();"));

    [Fact]
    public Task Unchecked_local_function_is_not_analyzed_and_calling_it_is_a_breach() =>
        Verifier.VerifyAsync(InHotMethod("[NoAllocUnchecked(\"setup only\")] void L() { var x = new object(); } {|NS013:L()|};"));

    [Fact]
    public Task Marked_local_function_in_plain_method_and_inside_lambda_is_analyzed() =>
        Verifier.VerifyAsync("""
            using System;
            using NeverSharp.NoAlloc;
            class C
            {
                void Outer()
                {
                    [NoAlloc] void L() { var x = {|NS001:new object()|}; }
                    void Plain() { var y = new object(); }
                    Action a = () => { [NoAlloc] void N() { var z = {|NS001:new object()|}; } N(); };
                    L(); Plain(); a();
                }
            }
            """);

    [Fact]
    public Task Async_is_reported_once_at_the_declaration() =>
        Verifier.VerifyAsync("""
            using System.Threading.Tasks;
            using NeverSharp.NoAlloc;
            class C
            {
                [NoAlloc(Constraints.All)] async Task {|NS005:A|}() { await {|NS008:Task.Yield()|}; }
                [NoAlloc] async Task {|NS001:B|}() { await {|NS008:Task.CompletedTask|}; }
            }
            [NoAlloc(Constraints.NoAsync)]
            class T
            {
                async Task {|NS005:D|}() { await {|NS008:Task.CompletedTask|}; }
            }
            """);

    [Fact]
    public Task Throw_lock_and_blocking_calls_are_reported_by_constraint() =>
        Verifier.VerifyAsync(InHotMethod(
            """
            if (i < 0) {|NS006:throw new InvalidOperationException();|}
            var x = o ?? {|NS006:throw new ArgumentNullException()|};
            lock ({|NS003:o|}) { }
            {|NS004:System.Threading.Thread.Sleep(1)|};
            {|NS003:System.Threading.Monitor.Enter(o)|};
            var r = {|NS004:T1.Result|};
            {|NS004:Mx.WaitOne()|};
            """,
            "Task<int> T1 = null!; System.Threading.Mutex Mx = null!;"));

    [Fact]
    public Task Allow_spin_lifts_spin_operations_only() =>
        Verifier.VerifyAsync("""
            using NeverSharp.NoAlloc;
            using System.Threading;
            class C
            {
                [NoAlloc(Constraints.All, Allow = Allow.Spin, Reason = "bounded spin before the handoff")]
                void Spins(object o) { var w = new SpinWait(); w.SpinOnce(); lock ({|NS003:o|}) { } }

                [NoAlloc(Constraints.All)]
                void Strict() { var w = new SpinWait(); {|NS003:w.SpinOnce()|}; }
            }
            """,
            new InMemoryAdditionalText("noalloc-allowlist.toml", """
                [[member]]
                symbol = "cs:*/M:System.Threading.SpinWait.SpinOnce"
                constraints = ["All"]
                reason = "a bounded spin"
                """));

    [Fact]
    public Task Reflection_and_activator_violate_NoDynamic() =>
        Verifier.VerifyAsync(InHotMethod(
            "var m = {|NS007:typeof(C).GetMethod(\"M\")|}; var x = {|NS007:Activator.CreateInstance(typeof(C))|};"));

    [Fact]
    public Task Access_to_a_type_with_an_explicit_static_constructor_violates_NoInit() =>
        Verifier.VerifyAsync(InHotMethod(
            "var v = {|NS011:WithInit.Value|}; {|NS011:WithInit.Run()|}; var w = Lazy.Value; {|NS011:WithInit.Own()|};",
            """
            class WithInit
            {
                static WithInit() { }
                public static int Value;
                [NoAlloc(Constraints.All)] public static void Run() { }
                [NoAlloc(Constraints.All)] public static void Own() { var v = Value; }
            }
            class Lazy { public static int Value = 1; }
            """));

    [Fact]
    public Task Try_catch_finally_is_clean() =>
        Verifier.VerifyAsync(InHotMethod("try { Hot(); } catch { } finally { Hot(); }"));

    [Fact]
    public Task Generated_code_is_not_analyzed() =>
        Verifier.VerifyAsync("""
            // <auto-generated/>
            using NeverSharp.NoAlloc;
            class C { [NoAlloc] void M() { var x = new object(); } }
            """);
}
