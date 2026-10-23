// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O, step O11 (variant B), token replay after a rolled-back speculation.
//
// No counterpart in scanner.ts or typescript-go's scanner: both scan the tokens of a lookahead or tryParse again
// after the parser rolls back. Here the last token a rolled-back speculation scanned is kept (recorded at the rollback,
// so recording costs nothing while the speculation runs), and the Scan that starts at its position again hands it out
// instead of scanning the characters a second time. Most lookaheads scan one token, which the parser scans next.
//
// Why it is exact. The effect of Scan is a function of the position it starts at, the scanning context (text, end of
// text, texts table, language version and variant, script kind, JSDoc parsing mode, skipped JSDoc asterisks) and,
// for tokenValue, the value before the call (Scan leaves it alone for punctuators). Everything else that changes the
// token state or the context, or has an effect beyond the token, increments `replaySerial`: the re-scans and the JSX
// and JSDoc token scans, ResetTokenState (also SetText, SetTextPos, ScanRange), the end of ScanRange, the setters of
// the context, UseTexts, an error (reported through the parser, whose diagnostics a rollback truncates) and an
// appended comment directive. A rolled-back speculation keeps its current token when the serial did not change while
// it ran (the token is then the result of a Scan, of a replay of one, or of a state a nested speculation restored, and
// that scan had no further effect) and the token starts at or after the speculation's position. It is handed out
// while the serial is unchanged (same context), by the Scan that starts at its position, and with the value rule:
// either the token's scan assigned its value (a non-empty span of the source within the token: values of earlier
// tokens lie before it), or the current value equals the kept value (then the scan, whether it leaves the value alone
// or assigns it, ends with that value). A value in the character arena created by the token's scan is not kept (the
// rollback truncates the arena). Scans before the position keep the token; a Scan at or beyond it drops it.

using System.Runtime.CompilerServices;
using NeverSharp.Generated;

namespace NeverSharp.Optimized;

internal sealed partial class Scanner
{
    // Not 0 while a kept token waits for the Scan at its position.
    private int replayGate;
    // Incremented by every change of the token state or the scanning context other than Scan and speculation.
    private int replaySerial;
    // The kept token: the state Scan leaves behind when it starts at replayFullStart, valid while replaySerial equals
    // replayAtSerial.
    private int replayAtSerial;
    private int replayFullStart;
    private int replayEnd;
    private int replayTokenStart;
    private SyntaxKind replayToken;
    private TokenFlags replayFlags;
    private TextRef replayValue;
    // The kept token's scan assigned replayValue; otherwise the token is only handed out where the current value is
    // replayValue.
    private bool replayValueAssigned;

    /// <summary>
    /// The end of a rolled-back speculation that started at <paramref name="savePos"/> with <paramref name="saveSerial"/>
    /// and <paramref name="saveTokenValue"/>: keeps the current token, the last one it scanned, for the Scan that starts
    /// at its position again (called before the state is restored).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private void KeepReplayToken(int savePos, int saveSerial, TextRef saveTokenValue)
    {
        if (fullStartPos < savePos || pos <= fullStartPos || replaySerial != saveSerial)
        {
            return;
        }
        // A non-empty span of the source at or after the token's start is a value this token's scan assigned (the
        // values of earlier tokens lie before it). Any other value is either the value the scan started with (left
        // alone) or one it assigned: the token is then handed out only where the current value is that value, which
        // makes both cases right. Such a value must outlive the rollback: a span of the source, or the value the
        // speculation started with.
        var assigned = tokenValue.Start >= tokenStart && tokenValue.Size > 1;
        if (assigned || tokenValue.Start >= 0 || (fullStartPos == savePos && tokenValue == saveTokenValue))
        {
            replayAtSerial = saveSerial;
            replayFullStart = fullStartPos;
            replayEnd = pos;
            replayTokenStart = tokenStart;
            replayToken = token;
            replayFlags = tokenFlags;
            replayValue = tokenValue;
            replayValueAssigned = assigned;
            replayGate = 1;
        }
    }

    /// <summary><see cref="Scan"/> while a kept token waits: hands it out if Scan starts at its position, else scans.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private SyntaxKind ScanWithReplay()
    {
        replayGate = 0;
        if (replaySerial == replayAtSerial)
        {
            if (pos == replayFullStart && (replayValueAssigned || tokenValue == replayValue))
            {
                fullStartPos = pos;
                tokenStart = replayTokenStart;
                pos = replayEnd;
                tokenFlags = replayFlags;
                tokenValue = replayValue;
                return token = replayToken;
            }
            if (pos < replayFullStart)
            {
                // Not there yet: scan (Scan, with the gate cleared), and keep the token.
                var kind = Scan();
                replayGate = 1;
                return kind;
            }
        }
        return Scan();
    }
}
