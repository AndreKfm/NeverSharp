// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// Portions Copyright 2026 Max Schwenk and the tsrs contributors
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/path.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tspath/path.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Optimized;

/// <summary>path.ts.</summary>
internal static partial class TsPath
{
    /// <summary>
    /// Internally, we represent paths as strings with '/' as the directory separator.
    /// When we make system calls (eg: LanguageServiceHost.getDirectory()),
    /// we expect the host to correctly handle paths in our specified format.
    /// </summary>
    internal const string DirectorySeparator = "/";
    internal const string AltDirectorySeparator = "\\";
    private const string UrlSchemeSeparator = "://";

    // path.ts: `const backslashRegExp = /\\/g;` Regular expressions are banned in Track F (Gate 2); its only use,
    // `path.replace(backslashRegExp, directorySeparator)`, replaces every backslash, which string.Replace does
    // (as strings.ReplaceAll does in tspath.NormalizeSlashes).

    //// Path Tests

    /// <summary>Determines whether a charCode corresponds to `/` or `\`.</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool IsAnyDirectorySeparator(int charCode)
    {
        return charCode == (int)CharacterCodes.slash || charCode == (int)CharacterCodes.backslash;
    }

    internal static bool FileExtensionIs(string path, string extension)
    {
        return path.Length > extension.Length && Core.EndsWith(path, extension);
    }

    /// <summary>Determines whether a path has a trailing separator (`/` or `\\`).</summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    internal static bool HasTrailingDirectorySeparator(string path)
    {
        return path.Length > 0 && IsAnyDirectorySeparator(path[path.Length - 1]);
    }

    //// Path Parsing

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool IsVolumeCharacter(int charCode)
    {
        return (charCode >= (int)CharacterCodes.a && charCode <= (int)CharacterCodes.z) ||
            (charCode >= (int)CharacterCodes.A && charCode <= (int)CharacterCodes.Z);
    }

    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int GetFileUrlVolumeSeparatorEnd(string url, int start)
    {
        var ch0 = CharCodeAt(url, start);
        if (ch0 == (int)CharacterCodes.colon)
        {
            return start + 1;
        }
        if (ch0 == (int)CharacterCodes.percent && CharCodeAt(url, start + 1) == (int)CharacterCodes._3)
        {
            var ch2 = CharCodeAt(url, start + 2);
            if (ch2 == (int)CharacterCodes.a || ch2 == (int)CharacterCodes.A)
            {
                return start + 3;
            }
        }
        return -1;
    }

    /// <summary>
    /// Returns length of the root part of a path or URL (i.e. length of "/", "x:/", "//server/share/, file:///user/files").
    /// If the root is part of a URL, the twos-complement of the root length is returned.
    /// </summary>
    private static int GetEncodedRootLength(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return 0;
        }
        int ch0 = path[0];

        // POSIX or UNC
        if (ch0 == (int)CharacterCodes.slash || ch0 == (int)CharacterCodes.backslash)
        {
            if (CharCodeAt(path, 1) != ch0)
            {
                return 1; // POSIX: "/" (or non-normalized "\")
            }

            var p1 = path.IndexOf(ch0 == (int)CharacterCodes.slash ? DirectorySeparator : AltDirectorySeparator, 2, StringComparison.Ordinal);
            if (p1 < 0)
            {
                return path.Length; // UNC: "//server" or "\\server"
            }

            return p1 + 1; // UNC: "//server/" or "\\server\"
        }

        // DOS
        if (IsVolumeCharacter(ch0) && CharCodeAt(path, 1) == (int)CharacterCodes.colon)
        {
            var ch2 = CharCodeAt(path, 2);
            if (ch2 == (int)CharacterCodes.slash || ch2 == (int)CharacterCodes.backslash)
            {
                return 3; // DOS: "c:/" or "c:\"
            }
            if (path.Length == 2)
            {
                return 2; // DOS: "c:" (but not "c:d")
            }
        }

        // URL
        var schemeEnd = path.IndexOf(UrlSchemeSeparator, StringComparison.Ordinal);
        if (schemeEnd != -1)
        {
            var authorityStart = schemeEnd + UrlSchemeSeparator.Length;
            var authorityEnd = path.IndexOf(DirectorySeparator, authorityStart, StringComparison.Ordinal);
            if (authorityEnd != -1)
            { // URL: "file:///", "file://server/", "file://server/path"
                // For local "file" URLs, include the leading DOS volume (if present).
                // Per https://www.ietf.org/rfc/rfc1738.txt, a host of "" or "localhost" is a
                // special case interpreted as "the machine from which the URL is being interpreted".
                var scheme = path[..schemeEnd];
                var authority = path[authorityStart..authorityEnd];
                if (
                    scheme == "file" && (authority.Length == 0 || authority == "localhost") &&
                    IsVolumeCharacter(CharCodeAt(path, authorityEnd + 1))
                )
                {
                    var volumeSeparatorEnd = GetFileUrlVolumeSeparatorEnd(path, authorityEnd + 2);
                    if (volumeSeparatorEnd != -1)
                    {
                        if (CharCodeAt(path, volumeSeparatorEnd) == (int)CharacterCodes.slash)
                        {
                            // URL: "file:///c:/", "file://localhost/c:/", "file:///c%3a/", "file://localhost/c%3a/"
                            return ~(volumeSeparatorEnd + 1);
                        }
                        if (volumeSeparatorEnd == path.Length)
                        {
                            // URL: "file:///c:", "file://localhost/c:", "file:///c$3a", "file://localhost/c%3a"
                            // but not "file:///c:d" or "file:///c%3ad"
                            return ~volumeSeparatorEnd;
                        }
                    }
                }
                return ~(authorityEnd + 1); // URL: "file://server/", "http://server/"
            }
            return ~path.Length; // URL: "file://server", "http://server"
        }

        // relative
        return 0;
    }

    /// <summary>
    /// Returns length of the root part of a path or URL (i.e. length of "/", "x:/", "//server/share/, file:///user/files").
    ///
    /// For example:
    /// <code>
    /// getRootLength("a") === 0                   // ""
    /// getRootLength("/") === 1                   // "/"
    /// getRootLength("c:") === 2                  // "c:"
    /// getRootLength("c:d") === 0                 // ""
    /// getRootLength("c:/") === 3                 // "c:/"
    /// getRootLength("c:\\") === 3                // "c:\\"
    /// getRootLength("//server") === 7            // "//server"
    /// getRootLength("//server/share") === 8      // "//server/"
    /// getRootLength("\\\\server") === 7          // "\\\\server"
    /// getRootLength("\\\\server\\share") === 8   // "\\\\server\\"
    /// getRootLength("file:///path") === 8        // "file:///"
    /// getRootLength("file:///c:") === 10         // "file:///c:"
    /// getRootLength("file:///c:d") === 8         // "file:///"
    /// getRootLength("file:///c:/path") === 11    // "file:///c:/"
    /// getRootLength("file://server") === 13      // "file://server"
    /// getRootLength("file://server/path") === 14 // "file://server/"
    /// getRootLength("http://server") === 13      // "http://server"
    /// getRootLength("http://server/path") === 14 // "http://server/"
    /// </code>
    /// </summary>
    internal static int GetRootLength(string path)
    {
        var rootLength = GetEncodedRootLength(path);
        return rootLength < 0 ? ~rootLength : rootLength;
    }

    /// <summary>
    /// Returns the path except for its containing directory name.
    /// Semantics align with NodeJS's `path.basename` except that we support URL's as well.
    /// </summary>
    internal static string GetBaseFileName(string path)
    {
        return GetBaseFileName(path, null, null, false);
    }

    /// <summary>
    /// Gets the portion of a path following the last (non-terminal) separator (`/`).
    /// Semantics align with NodeJS's `path.basename` except that we support URL's as well.
    /// If the base name has any one of the provided extensions, it is removed.
    /// </summary>
    internal static string GetBaseFileName(string path, string extensions, bool ignoreCase)
    {
        return GetBaseFileName(path, extensions, null, ignoreCase);
    }

    /// <summary>
    /// Gets the portion of a path following the last (non-terminal) separator (`/`).
    /// Semantics align with NodeJS's `path.basename` except that we support URL's as well.
    /// If the base name has any one of the provided extensions, it is removed.
    /// </summary>
    internal static string GetBaseFileName(string path, IReadOnlyList<string> extensions, bool ignoreCase)
    {
        return GetBaseFileName(path, null, extensions, ignoreCase);
    }

    // The implementation signature of getBaseFileName; `extensions?: string | readonly string[]` is split
    // into the two nullable parameters `extension` and `extensions` (no union types in C#).
    private static string GetBaseFileName(string path, string? extension, IReadOnlyList<string>? extensions, bool ignoreCase)
    {
        path = NormalizeSlashes(path);

        // if the path provided is itself the root, then it has not file name.
        var rootLength = GetRootLength(path);
        if (rootLength == path.Length)
        {
            return "";
        }

        // return the trailing portion of the path starting after the last (non-terminal) directory
        // separator but not including any trailing directory separator.
        path = RemoveTrailingDirectorySeparator(path);
        var name = path[Math.Max(GetRootLength(path), path.LastIndexOf(DirectorySeparator, StringComparison.Ordinal) + 1)..];
        var nameExtension = extension != null || extensions != null ? GetAnyExtensionFromPath(name, extension, extensions, ignoreCase) : null;
        return !string.IsNullOrEmpty(nameExtension) ? name[..(name.Length - nameExtension.Length)] : name;
    }

    private static string? TryGetExtensionFromPath(string path, string extension, Func<string, string, bool> stringEqualityComparer)
    {
        if (!Core.StartsWith(extension, "."))
        {
            extension = "." + extension;
        }
        if (path.Length >= extension.Length && path[path.Length - extension.Length] == (int)CharacterCodes.dot)
        {
            var pathExtension = path[(path.Length - extension.Length)..];
            if (stringEqualityComparer(pathExtension, extension))
            {
                return pathExtension;
            }
        }
        return null;
    }

    private static string GetAnyExtensionFromPathWorker(string path, string extensions, Func<string, string, bool> stringEqualityComparer)
    {
        var result = TryGetExtensionFromPath(path, extensions, stringEqualityComparer);
        return !string.IsNullOrEmpty(result) ? result : "";
    }

    private static string GetAnyExtensionFromPathWorker(string path, IReadOnlyList<string> extensions, Func<string, string, bool> stringEqualityComparer)
    {
        for (var i = 0; i < extensions.Count; i++)
        {
            var result = TryGetExtensionFromPath(path, extensions[i], stringEqualityComparer);
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }
        }
        return "";
    }

    /// <summary>
    /// Gets the file extension for a path.
    /// <code>
    /// getAnyExtensionFromPath("/path/to/file.ext") === ".ext"
    /// getAnyExtensionFromPath("/path/to/file.ext/") === ".ext"
    /// getAnyExtensionFromPath("/path/to/file") === ""
    /// getAnyExtensionFromPath("/path/to.ext/file") === ""
    /// </code>
    /// </summary>
    internal static string GetAnyExtensionFromPath(string path)
    {
        return GetAnyExtensionFromPath(path, null, null, false);
    }

    /// <summary>
    /// Gets the file extension for a path, provided it is one of the provided extensions.
    /// <code>
    /// getAnyExtensionFromPath("/path/to/file.ext", ".ext", true) === ".ext"
    /// getAnyExtensionFromPath("/path/to/file.js", ".ext", true) === ""
    /// getAnyExtensionFromPath("/path/to/file.js", [".ext", ".js"], true) === ".js"
    /// getAnyExtensionFromPath("/path/to/file.ext", ".EXT", false) === ""
    /// </code>
    /// </summary>
    internal static string GetAnyExtensionFromPath(string path, string extensions, bool ignoreCase)
    {
        return GetAnyExtensionFromPath(path, extensions, null, ignoreCase);
    }

    /// <summary>
    /// Gets the file extension for a path, provided it is one of the provided extensions.
    /// <code>
    /// getAnyExtensionFromPath("/path/to/file.ext", ".ext", true) === ".ext"
    /// getAnyExtensionFromPath("/path/to/file.js", ".ext", true) === ""
    /// getAnyExtensionFromPath("/path/to/file.js", [".ext", ".js"], true) === ".js"
    /// getAnyExtensionFromPath("/path/to/file.ext", ".EXT", false) === ""
    /// </code>
    /// </summary>
    internal static string GetAnyExtensionFromPath(string path, IReadOnlyList<string> extensions, bool ignoreCase)
    {
        return GetAnyExtensionFromPath(path, null, extensions, ignoreCase);
    }

    // The implementation signature of getAnyExtensionFromPath; `extensions?: string | readonly string[]` is
    // split into the two nullable parameters `extension` and `extensions` (no union types in C#).
    private static string GetAnyExtensionFromPath(string path, string? extension, IReadOnlyList<string>? extensions, bool ignoreCase)
    {
        // Retrieves any string from the final "." onwards from a base file name.
        // Unlike extensionFromPath, which throws an exception on unrecognized extensions.
        if (!string.IsNullOrEmpty(extension))
        {
            return GetAnyExtensionFromPathWorker(RemoveTrailingDirectorySeparator(path), extension, ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive);
        }
        if (extensions != null)
        {
            return GetAnyExtensionFromPathWorker(RemoveTrailingDirectorySeparator(path), extensions, ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive);
        }
        var baseFileName = GetBaseFileName(path);
        var extensionIndex = baseFileName.LastIndexOf('.');
        if (extensionIndex >= 0)
        {
            return baseFileName[extensionIndex..];
        }
        return "";
    }

    //// Path Normalization

    /// <summary>Normalize path separators, converting `\` into `/`.</summary>
    internal static string NormalizeSlashes(string path)
    {
        return path.Contains('\\', StringComparison.Ordinal)
            ? path.Replace('\\', '/')
            : path;
    }

    /// <summary>
    /// Combines paths. If a path is absolute, it replaces any previous path. Relative paths are not simplified.
    /// <code>
    /// // Non-rooted
    /// combinePaths("path", "to", "file.ext") === "path/to/file.ext"
    /// combinePaths("path", "dir", "..", "to", "file.ext") === "path/dir/../to/file.ext"
    /// // POSIX
    /// combinePaths("/path", "to", "file.ext") === "/path/to/file.ext"
    /// combinePaths("/path", "/to", "file.ext") === "/to/file.ext"
    /// // DOS
    /// combinePaths("c:/path", "to", "file.ext") === "c:/path/to/file.ext"
    /// combinePaths("c:/path", "c:/to", "file.ext") === "c:/to/file.ext"
    /// // URL
    /// combinePaths("file:///path", "to", "file.ext") === "file:///path/to/file.ext"
    /// combinePaths("file:///path", "file:///to", "file.ext") === "file:///to/file.ext"
    /// </code>
    /// </summary>
    internal static string CombinePaths(string path, string? relativePath)
    {
        // Module FE1 (Track O): the one-path case of combinePaths without the params array and with one concatenation
        // (tsrs-only shape: notes/perf-resolver-allocations.md, fewer allocations per resolution); the same result.
        if (!string.IsNullOrEmpty(path))
        {
            path = NormalizeSlashes(path);
        }
        if (string.IsNullOrEmpty(relativePath))
        {
            return path;
        }
        relativePath = NormalizeSlashes(relativePath);
        if (string.IsNullOrEmpty(path) || GetRootLength(relativePath) != 0)
        {
            return relativePath;
        }
        return HasTrailingDirectorySeparator(path) ? path + relativePath : string.Concat(path, DirectorySeparator, relativePath);
    }

    /// <inheritdoc cref="CombinePaths(string, string?)"/>
    internal static string CombinePaths(string path, params string?[] paths)
    {
        if (!string.IsNullOrEmpty(path))
        {
            path = NormalizeSlashes(path);
        }
        foreach (var p in paths)
        {
            var relativePath = p;
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }
            relativePath = NormalizeSlashes(relativePath);
            if (string.IsNullOrEmpty(path) || GetRootLength(relativePath) != 0)
            {
                path = relativePath;
            }
            else
            {
                path = EnsureTrailingDirectorySeparator(path) + relativePath;
            }
        }
        return path;
    }

    internal static string GetNormalizedAbsolutePath(string path, string? currentDirectory)
    {
        var rootLength = GetRootLength(path);
        if (rootLength == 0 && !string.IsNullOrEmpty(currentDirectory))
        {
            path = CombinePaths(currentDirectory, path);
            rootLength = GetRootLength(path);
        }
        else
        {
            // combinePaths normalizes slashes, so not necessary in the other branch
            path = NormalizeSlashes(path);
        }

        var simpleNormalized = SimpleNormalizePath(path);
        if (simpleNormalized != null)
        {
            return simpleNormalized.Length > rootLength ? RemoveTrailingDirectorySeparator(simpleNormalized) : simpleNormalized;
        }

        var length = path.Length;
        // `normalized` is only initialized once `path` is determined to be non-normalized. Module FE1 (Track O): it is
        // built in a per-thread buffer and made a string once at the end, instead of a new string per step (tsrs-only
        // shape, tsrs_module/src/scratch.rs with_path_buffer, notes/perf-resolver-allocations.md); every step below
        // is the string operation of path.ts on the buffer (a prefix of `path`, an append, a truncation), so the
        // result is the same.
        var normalized = normalizeBuffer ??= new NormalizeBuffer();
        normalized.Reset();
        var index = rootLength;
        var segmentStart = index;
        var normalizedUpTo = index;
        var seenNonDotDotSegment = rootLength != 0;
        while (index < length)
        {
            // At beginning of segment
            segmentStart = index;
            int ch = path[index];
            while (ch == (int)CharacterCodes.slash && index + 1 < length)
            {
                index++;
                ch = path[index];
            }
            if (index > segmentStart)
            {
                // Seen superfluous separator
                // (normalized ??= substring(path, 0, segmentStart - 1): String.prototype.substring clamps the end to
                // [0, length], and the start 0 is never after it)
                if (!normalized.IsSet)
                {
                    normalized.SetPrefix(path, Math.Min(Math.Max(segmentStart - 1, 0), length));
                }
                segmentStart = index;
            }
            // Past any superfluous separators
            var segmentEnd = path.IndexOf(DirectorySeparator, index + 1, StringComparison.Ordinal);
            if (segmentEnd == -1)
            {
                segmentEnd = length;
            }
            var segmentLength = segmentEnd - segmentStart;
            if (segmentLength == 1 && path[index] == (int)CharacterCodes.dot)
            {
                // "." segment (skip)
                if (!normalized.IsSet)
                {
                    normalized.SetPrefix(path, normalizedUpTo);
                }
            }
            else if (segmentLength == 2 && path[index] == (int)CharacterCodes.dot && CharCodeAt(path, index + 1) == (int)CharacterCodes.dot)
            {
                // ".." segment
                if (!seenNonDotDotSegment)
                {
                    if (normalized.IsSet)
                    {
                        normalized.Append(normalized.Length == rootLength ? ".." : "/..");
                    }
                    else
                    {
                        normalizedUpTo = index + 2;
                    }
                }
                else if (!normalized.IsSet)
                {
                    if (normalizedUpTo - 2 >= 0)
                    {
                        normalized.SetPrefix(path, Math.Max(rootLength, path.LastIndexOf('/', normalizedUpTo - 2)));
                    }
                    else
                    {
                        normalized.SetPrefix(path, normalizedUpTo);
                    }
                }
                else
                {
                    var lastSlash = normalized.LastIndexOfSlash();
                    if (lastSlash != -1)
                    {
                        normalized.Truncate(Math.Max(rootLength, lastSlash));
                    }
                    else
                    {
                        // (normalized = root)
                        normalized.SetPrefix(path, rootLength);
                    }
                    if (normalized.Length == rootLength)
                    {
                        seenNonDotDotSegment = rootLength != 0;
                    }
                }
            }
            else if (normalized.IsSet)
            {
                if (normalized.Length != rootLength)
                {
                    normalized.Append(DirectorySeparator);
                }
                seenNonDotDotSegment = true;
                normalized.Append(path.AsSpan(segmentStart, segmentEnd - segmentStart));
            }
            else
            {
                seenNonDotDotSegment = true;
                normalizedUpTo = segmentEnd;
            }
            index = segmentEnd + 1;
        }
        return normalized.IsSet ? normalized.ToString() : (length > rootLength ? RemoveTrailingDirectorySeparator(path) : path);
    }

    [ThreadStatic]
    private static NormalizeBuffer? normalizeBuffer;

    /// <summary>
    /// The `normalized` string of getNormalizedAbsolutePath as a growable character buffer (module FE1): unset until
    /// the first step that initializes it.
    /// </summary>
    private sealed class NormalizeBuffer
    {
        private char[] chars = new char[256];

        internal int Length { get; private set; }

        internal bool IsSet { get; private set; }

        internal void Reset()
        {
            Length = 0;
            IsSet = false;
        }

        /// <summary>normalized = path[..end]</summary>
        internal void SetPrefix(string path, int end)
        {
            Length = 0;
            IsSet = true;
            Append(path.AsSpan(0, end));
        }

        /// <summary>normalized += text</summary>
        internal void Append(ReadOnlySpan<char> text)
        {
            if (Length + text.Length > chars.Length)
            {
                Array.Resize(ref chars, Math.Max(chars.Length * 2, Length + text.Length));
            }
            text.CopyTo(chars.AsSpan(Length));
            Length += text.Length;
        }

        /// <summary>normalized = normalized[..length]</summary>
        internal void Truncate(int length) => Length = length;

        /// <summary>normalized.LastIndexOf('/')</summary>
        internal int LastIndexOfSlash() => chars.AsSpan(0, Length).LastIndexOf('/');

        public override string ToString() => new(chars, 0, Length);
    }

    internal static string NormalizePath(string path)
    {
        path = NormalizeSlashes(path);
        var normalized = SimpleNormalizePath(path);
        if (normalized != null)
        {
            return normalized;
        }
        normalized = GetNormalizedAbsolutePath(path, "");
        return !string.IsNullOrEmpty(normalized) && HasTrailingDirectorySeparator(path) ? EnsureTrailingDirectorySeparator(normalized) : normalized;
    }

    private static string? SimpleNormalizePath(string path)
    {
        // Most paths don't require normalization
        if (!RelativePathSegmentRegExpTest(path))
        {
            return path;
        }
        // Some paths only require cleanup of `/./` or leading `./`
        // (`path.replace(/\/\.\//g, "/")`: non-overlapping, left to right, as string.Replace.)
        var simplified = path.Replace("/./", "/", StringComparison.Ordinal);
        if (simplified.StartsWith("./", StringComparison.Ordinal))
        {
            simplified = simplified[2..];
        }
        if (simplified != path)
        {
            path = simplified;
            if (!RelativePathSegmentRegExpTest(path))
            {
                return path;
            }
        }
        return null;
    }

    //// Path Mutation

    /// <summary>
    /// Removes a trailing directory separator from a path, if it does not already have one.
    /// <code>
    /// removeTrailingDirectorySeparator("/path/to/file.ext") === "/path/to/file.ext"
    /// removeTrailingDirectorySeparator("/path/to/file.ext/") === "/path/to/file.ext"
    /// </code>
    /// </summary>
    internal static string RemoveTrailingDirectorySeparator(string path)
    {
        if (HasTrailingDirectorySeparator(path))
        {
            return path[..(path.Length - 1)];
        }

        return path;
    }

    /// <summary>
    /// Adds a trailing directory separator to a path, if it does not already have one.
    /// <code>
    /// ensureTrailingDirectorySeparator("/path/to/file.ext") === "/path/to/file.ext/"
    /// ensureTrailingDirectorySeparator("/path/to/file.ext/") === "/path/to/file.ext/"
    /// </code>
    /// </summary>
    internal static string EnsureTrailingDirectorySeparator(string path)
    {
        if (!HasTrailingDirectorySeparator(path))
        {
            return path + DirectorySeparator;
        }

        return path;
    }

    //// Path Comparisons

    /// <summary>
    /// path.ts: <c>relativePathSegmentRegExp.test(path)</c> with
    /// <c>const relativePathSegmentRegExp = /\/\/|(?:^|\/)\.\.?(?:$|\/)/;</c> (check path for these segments: '', '.'. '..').
    /// Regular expressions are banned in Track F (Gate 2), so the pattern is matched by hand, as
    /// typescript-go does in hasRelativePathSegment (internal/tspath/path.go): true if the path contains
    /// "//", or a "." or ".." segment that starts at the beginning or after a "/" and ends at the end or
    /// before a "/".
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static bool RelativePathSegmentRegExpTest(string path)
    {
        var length = path.Length;
        for (var i = 0; i < length; i++)
        {
            int ch = path[i];
            if (ch == (int)CharacterCodes.slash)
            {
                // `\/\/`
                if (i + 1 < length && path[i + 1] == (int)CharacterCodes.slash)
                {
                    return true;
                }
            }
            else if (ch == (int)CharacterCodes.dot && (i == 0 || path[i - 1] == (int)CharacterCodes.slash))
            {
                // `(?:^|\/)\.\.?(?:$|\/)`: "." then end or "/" ...
                if (i + 1 == length || path[i + 1] == (int)CharacterCodes.slash)
                {
                    return true;
                }
                // ... or ".." then end or "/"
                if (path[i + 1] == (int)CharacterCodes.dot && (i + 2 == length || path[i + 2] == (int)CharacterCodes.slash))
                {
                    return true;
                }
            }
        }
        return false;
    }

    //// JavaScript string semantics the ported functions rely on.

    /// <summary>
    /// String.prototype.charCodeAt: NaN for an index outside the string. -1 stands for NaN: like NaN it is
    /// unequal to every character code and outside every character range.
    /// </summary>
    [NoAlloc(Constraints.NoLock | Constraints.NoBlock)]
    private static int CharCodeAt(string text, int index)
    {
        return (uint)index < (uint)text.Length ? text[index] : -1;
    }
}
