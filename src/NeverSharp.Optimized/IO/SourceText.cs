// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
// Original code (not ported): Track O's file reading (step O1, variant B: pooled bytes, char arena), with the
// semantics of ts.sys.readFile (sys.ts): byte order marks decide the encoding (UTF-16 BE, UTF-16 LE, UTF-8), UTF-8
// otherwise.
//
// The contract for the tools: SourceText.Read(path) returns the file's text, Chars exposes it as UTF-16 code units,
// and Parser.ParseFile reads through it. The reader check (tools/NeverSharp.ReaderCheck) compares Chars with the
// sys.ts semantics on every corpus file.

using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using Microsoft.Win32.SafeHandles;

namespace NeverSharp.Optimized;

/// <summary>
/// The text of a source file as Track O reads it: an array of UTF-16 code units of the exact length (no
/// <see cref="string"/>), the text type of the scanner, the text table and the SourceFile node.
/// <para>
/// Reading (variant B): the file's bytes go into a per-thread buffer that is reused from file to file (grown to
/// the largest file), so no byte array is allocated per file; they are decoded straight into the character array,
/// which is the only allocation per file and belongs to the parse result (the node store keeps it; nothing pooled
/// is referenced after the read, so results stay valid and independent of later reads, on any thread). The character
/// array lives on the pinned object heap, as the node store's final records do: it outlives the parse like the tree,
/// holds no references, and the collector neither copies it from generation to generation nor scans it.
/// </para>
/// <para>
/// Decoding: UTF-8 with <see cref="Utf8.ToUtf16"/> (vectorized, with its own ASCII fast path), the exact length
/// counted first by <see cref="Encoding.UTF8"/> (invalid sequences become U+FFFD as Encoding.UTF8 replaces them);
/// UTF-16 by copying or byte-swapping the code units, and through <see cref="Encoding.Unicode"/> or
/// <see cref="Encoding.BigEndianUnicode"/> only when the text has surrogates (those encodings replace unpaired
/// surrogates by U+FFFD); an odd trailing byte of UTF-16 is dropped.
/// </para>
/// </summary>
internal readonly struct SourceText
{
    /// <summary>The empty text (one shared array of length 0).</summary>
    internal static readonly char[] EmptyChars = [];

    private readonly char[] chars;

    private SourceText(char[] chars) => this.chars = chars;

    /// <summary>The file's text as UTF-16 code units.</summary>
    internal ReadOnlySpan<char> Chars => chars;

    internal int Length => chars.Length;

    /// <summary>The text as the parser takes it: the character array of the exact length (owned by the result).</summary>
    internal char[] Array => chars;

    /// <summary>A text given as a string (the dump tool, the tests): its characters copied into an array.</summary>
    internal static SourceText FromString(string? text) => new(string.IsNullOrEmpty(text) ? EmptyChars : text.ToCharArray());

    /// <summary>sys.ts readFile: byte order marks decide the encoding, UTF-8 otherwise.</summary>
    internal static SourceText Read(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var fileLength = RandomAccess.GetLength(handle);
        if (fileLength > System.Array.MaxLength)
        {
            throw new IOException($"The file is too long: {path}");
        }
        var read = ReadAll(handle, (int)fileLength);
        return new SourceText(Decode(threadBuffer.AsSpan(0, read)));
    }

    // The thread's read buffer (variant B's byte pool): one buffer per thread, reused from file to file and grown
    // (powers of two from 64 KiB) to the largest file the thread has read. A thread reads one file at a time and
    // decodes its bytes before the next read, so the buffer is never shared, and the decoded text does not refer to it.
    // (Not ArrayPool<byte>.Shared: it does not keep the arrays of the few files over 1 MiB, and its trimming after full
    // collections drops buffers that are then allocated again. Not pinned: a pinned-heap allocation per new pool
    // thread measured slower.)
    private const int MinThreadBuffer = 1 << 16;

    [ThreadStatic]
    private static byte[]? threadBuffer;

    // The thread's buffer with room for at least the given number of bytes (the first bytes kept when it grows).
    private static byte[] ThreadBuffer(int length, int keep)
    {
        var buffer = threadBuffer;
        if (buffer is null || buffer.Length < length)
        {
            var size = (int)Math.Min(BitOperations.RoundUpToPowerOf2((uint)Math.Max(length, MinThreadBuffer)), (uint)System.Array.MaxLength);
            var grown = GC.AllocateUninitializedArray<byte>(size);
            buffer?.AsSpan(0, keep).CopyTo(grown);
            threadBuffer = buffer = grown;
        }
        return buffer;
    }

    // Reads the file into the thread's buffer, as File.ReadAllBytes does: the length found when the file was opened
    // (a file that shrinks meanwhile ends early), or, when the length is unknown (0), everything up to the end of the
    // file, the buffer growing as needed. Returns the number of bytes.
    private static int ReadAll(SafeFileHandle handle, int expected)
    {
        var read = 0;
        if (expected > 0)
        {
            var buffer = ThreadBuffer(expected, 0);
            while (read < expected)
            {
                var n = RandomAccess.Read(handle, buffer.AsSpan(read, expected - read), read);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }
            return read;
        }
        while (true)
        {
            var buffer = ThreadBuffer(read + 1, read);
            var n = RandomAccess.Read(handle, buffer.AsSpan(read), read);
            if (n == 0)
            {
                return read;
            }
            read += n;
        }
    }

    private static char[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return DecodeUtf16(bytes.Slice(2, (bytes.Length - 2) & ~1), bigEndian: true);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return DecodeUtf16(bytes.Slice(2, (bytes.Length - 2) & ~1), bigEndian: false);
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes.Slice(3);
        }
        return DecodeUtf8(bytes);
    }

    private static char[] DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return EmptyChars;
        }
        var count = Encoding.UTF8.GetCharCount(bytes);
        var chars = GC.AllocateUninitializedArray<char>(count, pinned: true);
        var status = Utf8.ToUtf16(bytes, chars, out var bytesRead, out var charsWritten, replaceInvalidSequences: true, isFinalBlock: true);
        if (status != OperationStatus.Done || bytesRead != bytes.Length || charsWritten != count)
        {
            // (Not expected: Utf8.ToUtf16 replaces invalid sequences as Encoding.UTF8 does. Kept exact regardless.)
            Encoding.UTF8.GetChars(bytes, chars);
        }
        return chars;
    }

    private static char[] DecodeUtf16(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        if (bytes.Length == 0)
        {
            return EmptyChars;
        }
        var units = MemoryMarshal.Cast<byte, ushort>(bytes);
        var chars = GC.AllocateUninitializedArray<char>(units.Length, pinned: true);
        var target = MemoryMarshal.Cast<char, ushort>(chars.AsSpan());
        if (bigEndian == BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(units, target);
        }
        else
        {
            units.CopyTo(target);
        }
        if (chars.AsSpan().ContainsAnyInRange('\uD800', '\uDFFF'))
        {
            // Surrogates: decoded as the encodings do, which replace unpaired ones by U+FFFD.
            var encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
            var exact = GC.AllocateUninitializedArray<char>(encoding.GetCharCount(bytes), pinned: true);
            encoding.GetChars(bytes, exact);
            return exact;
        }
        return chars;
    }
}
