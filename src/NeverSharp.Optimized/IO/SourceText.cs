// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's file reading (step O1, variant C: native text), with the semantics of
// ts.sys.readFile (sys.ts): byte order marks decide the encoding (UTF-16 BE, UTF-16 LE, UTF-8), UTF-8 otherwise.
//
// Reading: the file's bytes go into a per-thread buffer in native memory (RandomAccess on a file handle of known
// length; memory-mapping each file measured 2.5 times slower on the tuning corpus, whose files are small). Decoding:
// System.Text.Unicode.Utf8.ToUtf16 (vectorized; invalid sequences become U+FFFD exactly as Encoding.UTF8 replaces them),
// for the UTF-16 byte order marks Encoding.Unicode / Encoding.BigEndianUnicode into the destination span (the same
// replacement of lone surrogates as the reference decode). No byte[] and no string per file.
//
// Where the text lives: a parse's text is NativeText, UTF-16 in memory from NativeMemory.Alloc, owned by the parse
// result (the store's TextTable and the SourceFile node) and released by it (SourceFile.Release), with a finalizer as
// the backstop. The scanner and the text table read it through SourceChars (pointer and length). SourceText.Read, the
// tool contract (the reader check), decodes with the same code into a managed array, so that a span of it keeps it alive.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace NeverSharp.Optimized;

/// <summary>The text of a source file as Track O reads it (the tool contract: the reader check compares <see cref="Chars"/>).</summary>
internal readonly struct SourceText
{
    private readonly char[] chars;
    private readonly int length;

    private SourceText(char[] chars, int length)
    {
        this.chars = chars;
        this.length = length;
    }

    /// <summary>The file's text as UTF-16 code units.</summary>
    internal ReadOnlySpan<char> Chars => new(chars, 0, length);

    internal int Length => length;

    /// <summary>sys.ts readFile: byte order marks decide the encoding, UTF-8 otherwise (decoded as for a parse, into a managed array).</summary>
    internal static unsafe SourceText Read(string path)
    {
        var bytes = FileBytes.Read(path, out var count);
        try
        {
            var source = new ReadOnlySpan<byte>(bytes, count);
            var chars = GC.AllocateUninitializedArray<char>(MaxChars(source));
            return new SourceText(chars, Decode(source, chars));
        }
        finally
        {
            FileBytes.Done(bytes);
        }
    }

    /// <summary>sys.ts readFile into native memory: the text of a parse (<see cref="Parser.ParseFile"/>).</summary>
    internal static unsafe NativeText ReadNative(string path)
    {
        var bytes = FileBytes.Read(path, out var count);
        try
        {
            var source = new ReadOnlySpan<byte>(bytes, count);
            var capacity = MaxChars(source);
            // One more character: a terminating NUL after the text.
            var chars = (char*)NativeMemory.Alloc((nuint)capacity + 1, sizeof(char));
            int length;
            try
            {
                length = Decode(source, new Span<char>(chars, capacity));
            }
            catch
            {
                NativeMemory.Free(chars);
                throw;
            }
            if (length < capacity / 2)
            {
                // Mostly non-ASCII text (several bytes per character): give the unused room back.
                chars = (char*)NativeMemory.Realloc(chars, ((nuint)length + 1) * sizeof(char));
            }
            chars[length] = '\0';
            return new NativeText(chars, length);
        }
        finally
        {
            FileBytes.Done(bytes);
        }
    }

    // The number of UTF-16 code units the bytes decode to at most.
    private static int MaxChars(ReadOnlySpan<byte> bytes)
    {
        if (IsUtf16(bytes))
        {
            return (bytes.Length - 2) >> 1;
        }
        return bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUtf16(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && ((bytes[0] == 0xFE && bytes[1] == 0xFF) || (bytes[0] == 0xFF && bytes[1] == 0xFE));

    // sys.ts readFile's decoding into the destination (at least MaxChars long); returns the number of code units.
    private static int Decode(ReadOnlySpan<byte> bytes, Span<char> destination)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            // UTF-16 BE (an odd trailing byte is dropped).
            return Encoding.BigEndianUnicode.GetChars(bytes.Slice(2, (bytes.Length - 2) & ~1), destination);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            // UTF-16 LE (an odd trailing byte is dropped).
            return Encoding.Unicode.GetChars(bytes.Slice(2, (bytes.Length - 2) & ~1), destination);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes.Slice(3);
        }
        // UTF-8: invalid sequences are replaced by U+FFFD (as Encoding.UTF8 does), so the decoding always completes.
        var status = Utf8.ToUtf16(bytes, destination, out _, out var written, replaceInvalidSequences: true, isFinalBlock: true);
        if (status != System.Buffers.OperationStatus.Done)
        {
            throw new InvalidOperationException("UTF-8 decoding did not complete: " + status.ToString());
        }
        return written;
    }

    // The bytes of a file: read into a buffer of the thread in native memory (reused from file to file; files above
    // MaxKept get a buffer of their own, freed after decoding).
    private static unsafe class FileBytes
    {
        private const int MaxKept = 4 * 1024 * 1024;

        [ThreadStatic]
        private static byte* buffer;

        [ThreadStatic]
        private static int capacity;

        internal static byte* Read(string path, out int count)
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
            var fileLength = RandomAccess.GetLength(handle);
            if (fileLength > Array.MaxLength)
            {
                throw new IOException("File too long: " + path);
            }
            var length = (int)fileLength;
            byte* bytes;
            if (length > MaxKept)
            {
                bytes = (byte*)NativeMemory.Alloc((nuint)length);
            }
            else
            {
                if (capacity < length || buffer == null)
                {
                    NativeMemory.Free(buffer);
                    buffer = null;
                    var size = Math.Max(64 * 1024, (int)Math.Min(MaxKept, BitOperations.RoundUpToPowerOf2((uint)length)));
                    buffer = (byte*)NativeMemory.Alloc((nuint)size);
                    capacity = size;
                }
                bytes = buffer;
            }
            try
            {
                var read = 0;
                while (read < length)
                {
                    var n = RandomAccess.Read(handle, new Span<byte>(bytes + read, length - read), read);
                    if (n == 0)
                    {
                        break;
                    }
                    read += n;
                }
                count = read;
                return bytes;
            }
            catch
            {
                Done(bytes);
                throw;
            }
        }

        /// <summary>The bytes are decoded: a buffer of the file's own is freed.</summary>
        internal static void Done(byte* bytes)
        {
            if (bytes != buffer)
            {
                NativeMemory.Free(bytes);
            }
        }
    }
}

/// <summary>
/// The source text of a parse (variant C of step O1): UTF-16 code units in native memory, or a pinned string (the
/// texts <see cref="Parser.CreateSourceFile(string, string, CreateSourceFileOptions, bool, NeverSharp.Generated.ScriptKind?)"/>
/// gets). Owned by the parse result (its TextTable and SourceFile node), which releases it (<see cref="Release"/>,
/// SourceFile.Release); the finalizer is the backstop when a result is dropped without that. Whoever reads the
/// characters through <see cref="Chars"/> keeps the owner (the result, its store) reachable meanwhile.
/// </summary>
internal sealed unsafe class NativeText
{
    // The characters (0 once released); a pinned string's when pin is allocated, NativeMemory's otherwise.
    private nint address;
    private int length;
    private GCHandle pin;
    private readonly string? text;

    /// <summary>The empty text (never released).</summary>
    internal static readonly NativeText Empty = new(string.Empty);

    internal NativeText(char* chars, int length)
    {
        address = (nint)chars;
        this.length = length;
    }

    private NativeText(string text)
    {
        this.text = text;
        pin = GCHandle.Alloc(text, GCHandleType.Pinned);
        address = pin.AddrOfPinnedObject();
        length = text.Length;
    }

    ~NativeText()
    {
        Free();
    }

    /// <summary>A string as the text of a parse: pinned, not copied (<see cref="Empty"/> for null or "").</summary>
    internal static NativeText FromString(string? text) => string.IsNullOrEmpty(text) ? Empty : new NativeText(text);

    /// <summary>The characters.</summary>
    internal SourceChars Chars
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => new((char*)address, length);
    }

    internal int Length
    {
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => length;
    }

    /// <summary>The text as a string (the pinned string itself, or a copy of the native characters).</summary>
    internal string GetString() => text ?? new string((char*)address, 0, length);

    /// <summary>Frees the characters now (the result that owns the text is done with it); later reads see an empty text.</summary>
    internal void Release()
    {
        if (ReferenceEquals(this, Empty))
        {
            return;
        }
        // (The finalizer still runs later and finds nothing to free.)
        Free();
    }

    private void Free()
    {
        var chars = Interlocked.Exchange(ref address, 0);
        length = 0;
        if (chars == 0)
        {
            return;
        }
        if (pin.IsAllocated)
        {
            pin.Free();
        }
        else
        {
            NativeMemory.Free((void*)chars);
        }
    }
}

/// <summary>
/// The characters of a source text as the scanner, the parser and the text table read them (variant C of step O1): a
/// pointer and a length, read without bounds checks (the callers keep the bounds, as they did for the string); valid
/// while the <see cref="NativeText"/> it comes from is.
/// </summary>
internal readonly unsafe struct SourceChars
{
    private readonly char* chars;

    /// <summary>The number of UTF-16 code units.</summary>
    internal readonly int Length;

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal SourceChars(char* chars, int length)
    {
        this.chars = chars;
        Length = length;
    }

    /// <summary>`text[index]` for 0 &lt;= index &lt;= Length (unchecked; at Length the terminating NUL of a native text).</summary>
    internal char this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
        get => chars[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<char> AsSpan() => new(chars, Length);

    /// <summary>The characters from <paramref name="start"/> on (0 &lt;= start &lt;= Length).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<char> AsSpan(int start)
    {
        Debug.Assert((uint)start <= (uint)Length, "SourceChars range out of range");
        return new(chars + start, Length - start);
    }

    /// <summary>The characters from <paramref name="start"/> on, <paramref name="length"/> of them (within the text).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal ReadOnlySpan<char> AsSpan(int start, int length)
    {
        Debug.Assert((ulong)(uint)start + (uint)length <= (uint)Length, "SourceChars range out of range");
        return new(chars + start, length);
    }

    /// <summary>Whether both are the same characters (the same text).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal bool SameAs(SourceChars other) => chars == other.chars && Length == other.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    public static implicit operator ReadOnlySpan<char>(SourceChars text) => new(text.chars, text.Length);

    public override string ToString() => new(chars, 0, Length);
}
