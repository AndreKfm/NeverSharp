// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/path.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/utilities.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tspath/path.go and internal/tspath/extension.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>The functions of path.ts that the parser reaches (path.ts is <c>TsPath</c> because C# has <c>System.IO.Path</c>).</summary>
internal static partial class TsPath
{
    /// <summary>
    /// Internally, we represent paths as strings with '/' as the directory separator.
    /// When we make system calls (eg: LanguageServiceHost.getDirectory()),
    /// we expect the host to correctly handle paths in our specified format.
    /// </summary>
    // path.ts:28
    internal const string DirectorySeparator = "/";

    // path.ts:30
    internal const string AltDirectorySeparator = "\\";

    // path.ts:31
    private const string UrlSchemeSeparator = "://";

    // path.ts:32 `backslashRegExp = /\\/g` is used for `replace(backslashRegExp, "/")` only: see NormalizeSlashes.

    //// Path Tests

    /// <summary>Determines whether a charCode corresponds to `/` or `\`.</summary>
    // path.ts:41
    internal static bool IsAnyDirectorySeparator(int charCode)
    {
        return charCode == (int)CharacterCodes.slash || charCode == (int)CharacterCodes.backslash;
    }

    // path.ts:120
    internal static bool FileExtensionIs(string path, string extension)
    {
        return path.Length > extension.Length && Core.EndsWith(path, extension);
    }

    /// <summary>Determines whether a path has a trailing separator (`/` or `\\`).</summary>
    // path.ts:140
    internal static bool HasTrailingDirectorySeparator(string path)
    {
        return path.Length > 0 && IsAnyDirectorySeparator(path[path.Length - 1]);
    }

    //// Path Parsing

    // path.ts:146
    private static bool IsVolumeCharacter(int charCode)
    {
        return (charCode >= (int)CharacterCodes.a && charCode <= (int)CharacterCodes.z) ||
            (charCode >= (int)CharacterCodes.A && charCode <= (int)CharacterCodes.Z);
    }

    // path.ts:151
    private static int GetFileUrlVolumeSeparatorEnd(string url, int start)
    {
        var ch0 = Core.CharCodeAt(url, start);
        if (ch0 == (int)CharacterCodes.colon)
        {
            return start + 1;
        }
        if (ch0 == (int)CharacterCodes.percent && Core.CharCodeAt(url, start + 1) == (int)CharacterCodes._3)
        {
            var ch2 = Core.CharCodeAt(url, start + 2);
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
    // path.ts:165
    private static int GetEncodedRootLength(string path)
    {
        if (path.Length == 0)
        {
            return 0;
        }
        var ch0 = Core.CharCodeAt(path, 0);

        // POSIX or UNC
        if (ch0 == (int)CharacterCodes.slash || ch0 == (int)CharacterCodes.backslash)
        {
            if (Core.CharCodeAt(path, 1) != ch0)
            {
                return 1; // POSIX: "/" (or non-normalized "\")
            }

            var p1 = path.IndexOf(ch0 == (int)CharacterCodes.slash ? '/' : '\\', 2);
            if (p1 < 0)
            {
                return path.Length; // UNC: "//server" or "\\server"
            }

            return p1 + 1; // UNC: "//server/" or "\\server\"
        }

        // DOS
        if (IsVolumeCharacter(ch0) && Core.CharCodeAt(path, 1) == (int)CharacterCodes.colon)
        {
            var ch2 = Core.CharCodeAt(path, 2);
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
            var authorityEnd = path.IndexOf('/', authorityStart);
            if (authorityEnd != -1)
            { // URL: "file:///", "file://server/", "file://server/path"
                // For local "file" URLs, include the leading DOS volume (if present).
                // Per https://www.ietf.org/rfc/rfc1738.txt, a host of "" or "localhost" is a
                // special case interpreted as "the machine from which the URL is being interpreted".
                var scheme = path.Substring(0, schemeEnd);
                var authority = path.Substring(authorityStart, authorityEnd - authorityStart);
                if (
                    scheme == "file" && (authority.Length == 0 || authority == "localhost") &&
                    IsVolumeCharacter(Core.CharCodeAt(path, authorityEnd + 1)))
                {
                    var volumeSeparatorEnd = GetFileUrlVolumeSeparatorEnd(path, authorityEnd + 2);
                    if (volumeSeparatorEnd != -1)
                    {
                        if (Core.CharCodeAt(path, volumeSeparatorEnd) == (int)CharacterCodes.slash)
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
    /// For example: GetRootLength("a") == 0, "/" 1, "c:" 2, "c:d" 0, "c:/" 3, "c:\\" 3, "//server" 7,
    /// "//server/share" 8, "file:///path" 8, "file:///c:" 10, "file:///c:d" 8, "file:///c:/path" 11,
    /// "file://server" 13, "file://server/path" 14, "http://server" 13, "http://server/path" 14.
    /// </summary>
    // path.ts:250
    internal static int GetRootLength(string path)
    {
        var rootLength = GetEncodedRootLength(path);
        return rootLength < 0 ? ~rootLength : rootLength;
    }

    /// <summary>
    /// Gets the portion of a path following the last (non-terminal) separator (`/`).
    /// Semantics align with NodeJS's `path.basename` except that we support URL's as well.
    /// </summary>
    // path.ts:356 (the overload without extensions)
    internal static string GetBaseFileName(string path)
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
        return path.Substring(Math.Max(GetRootLength(path), path.LastIndexOf('/') + 1));
    }

    /// <summary>
    /// Gets the portion of a path following the last (non-terminal) separator (`/`).
    /// If the base name has the provided extension, it is removed.
    /// </summary>
    // path.ts:356 (the overload with an extension)
    internal static string GetBaseFileName(string path, string extensions, bool ignoreCase)
    {
        var name = GetBaseFileName(path);
        var extension = GetAnyExtensionFromPath(name, extensions, ignoreCase);
        return extension.Length > 0 ? name.Substring(0, name.Length - extension.Length) : name;
    }

    /// <summary>
    /// Gets the portion of a path following the last (non-terminal) separator (`/`).
    /// If the base name has any one of the provided extensions, it is removed.
    /// </summary>
    // path.ts:356 (the overload with several extensions)
    internal static string GetBaseFileName(string path, IReadOnlyList<string> extensions, bool ignoreCase)
    {
        var name = GetBaseFileName(path);
        var extension = GetAnyExtensionFromPath(name, extensions, ignoreCase);
        return extension.Length > 0 ? name.Substring(0, name.Length - extension.Length) : name;
    }

    // path.ts:388
    private static string? TryGetExtensionFromPath(string path, string extension, Func<string, string, bool> stringEqualityComparer)
    {
        if (!Core.StartsWith(extension, "."))
        {
            extension = "." + extension;
        }
        if (path.Length >= extension.Length && path[path.Length - extension.Length] == '.')
        {
            var pathExtension = path.Substring(path.Length - extension.Length);
            if (stringEqualityComparer(pathExtension, extension))
            {
                return pathExtension;
            }
        }
        return null;
    }

    // path.ts:398 (the case of a single extension)
    private static string GetAnyExtensionFromPathWorker(string path, string extensions, Func<string, string, bool> stringEqualityComparer)
    {
        var result = TryGetExtensionFromPath(path, extensions, stringEqualityComparer);
        return Core.IsTruthy(result) ? result! : "";
    }

    // path.ts:398 (the case of several extensions)
    private static string GetAnyExtensionFromPathWorker(string path, IReadOnlyList<string> extensions, Func<string, string, bool> stringEqualityComparer)
    {
        foreach (var extension in extensions)
        {
            var result = TryGetExtensionFromPath(path, extension, stringEqualityComparer);
            if (Core.IsTruthy(result))
            {
                return result!;
            }
        }
        return "";
    }

    /// <summary>
    /// Gets the file extension for a path.
    /// GetAnyExtensionFromPath("/path/to/file.ext") == ".ext", ("/path/to/file.ext/") == ".ext",
    /// ("/path/to/file") == "", ("/path/to.ext/file") == "".
    /// </summary>
    // path.ts:421 (the overload without extensions)
    internal static string GetAnyExtensionFromPath(string path)
    {
        // Retrieves any string from the final "." onwards from a base file name.
        // Unlike extensionFromPath, which throws an exception on unrecognized extensions.
        var baseFileName = GetBaseFileName(path);
        var extensionIndex = baseFileName.LastIndexOf('.');
        if (extensionIndex >= 0)
        {
            return baseFileName.Substring(extensionIndex);
        }
        return "";
    }

    /// <summary>Gets the file extension for a path, provided it is one of the provided extensions (an empty string selects any extension).</summary>
    // path.ts:421 (the overload with one extension)
    internal static string GetAnyExtensionFromPath(string path, string extensions, bool ignoreCase)
    {
        if (extensions.Length > 0)
        {
            return GetAnyExtensionFromPathWorker(RemoveTrailingDirectorySeparator(path), extensions, ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive);
        }
        return GetAnyExtensionFromPath(path);
    }

    /// <summary>Gets the file extension for a path, provided it is one of the provided extensions.</summary>
    // path.ts:421 (the overload with several extensions)
    internal static string GetAnyExtensionFromPath(string path, IReadOnlyList<string> extensions, bool ignoreCase)
    {
        return GetAnyExtensionFromPathWorker(RemoveTrailingDirectorySeparator(path), extensions, ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive);
    }

    //// Path Normalization

    /// <summary>Normalize path separators, converting `\` into `/`.</summary>
    // path.ts:526
    internal static string NormalizeSlashes(string path)
    {
        return path.Contains('\\')
            ? path.Replace('\\', '/')
            : path;
    }

    /// <summary>
    /// Combines paths. If a path is absolute, it replaces any previous path. Relative paths are not simplified.
    /// CombinePaths("path", "to", "file.ext") == "path/to/file.ext"; CombinePaths("/path", "/to", "file.ext") == "/to/file.ext".
    /// </summary>
    // path.ts:579
    internal static string CombinePaths(string path, params string?[] paths)
    {
        if (path.Length > 0)
        {
            path = NormalizeSlashes(path);
        }
        foreach (var rawRelativePath in paths)
        {
            if (string.IsNullOrEmpty(rawRelativePath))
            {
                continue;
            }
            var relativePath = NormalizeSlashes(rawRelativePath);
            if (path.Length == 0 || GetRootLength(relativePath) != 0)
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

    /// <summary>JavaScript's <c>text.substring(start, end)</c>: both positions are clamped to the string, and swapped if reversed.</summary>
    private static string Substring(string text, int start, int end)
    {
        start = Math.Min(Math.Max(start, 0), text.Length);
        end = Math.Min(Math.Max(end, 0), text.Length);
        return start <= end ? text.Substring(start, end - start) : text.Substring(end, start - end);
    }

    // path.ts:627
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
        if (simpleNormalized is not null)
        {
            return simpleNormalized.Length > rootLength ? RemoveTrailingDirectorySeparator(simpleNormalized) : simpleNormalized;
        }

        var length = path.Length;
        var root = Substring(path, 0, rootLength);
        // `normalized` is only initialized once `path` is determined to be non-normalized
        string? normalized = null;
        var index = rootLength;
        var segmentStart = index;
        var normalizedUpTo = index;
        var seenNonDotDotSegment = rootLength != 0;
        while (index < length)
        {
            // At beginning of segment
            segmentStart = index;
            var ch = Core.CharCodeAt(path, index);
            while (ch == (int)CharacterCodes.slash && index + 1 < length)
            {
                index++;
                ch = Core.CharCodeAt(path, index);
            }
            if (index > segmentStart)
            {
                // Seen superfluous separator
                normalized ??= Substring(path, 0, segmentStart - 1);
                segmentStart = index;
            }
            // Past any superfluous separators
            var segmentEnd = path.IndexOf('/', index + 1);
            if (segmentEnd == -1)
            {
                segmentEnd = length;
            }
            var segmentLength = segmentEnd - segmentStart;
            if (segmentLength == 1 && Core.CharCodeAt(path, index) == (int)CharacterCodes.dot)
            {
                // "." segment (skip)
                normalized ??= Substring(path, 0, normalizedUpTo);
            }
            else if (segmentLength == 2 && Core.CharCodeAt(path, index) == (int)CharacterCodes.dot && Core.CharCodeAt(path, index + 1) == (int)CharacterCodes.dot)
            {
                // ".." segment
                if (!seenNonDotDotSegment)
                {
                    if (normalized is not null)
                    {
                        normalized += normalized.Length == rootLength ? ".." : "/..";
                    }
                    else
                    {
                        normalizedUpTo = index + 2;
                    }
                }
                else if (normalized is null)
                {
                    if (normalizedUpTo - 2 >= 0)
                    {
                        normalized = Substring(path, 0, Math.Max(rootLength, path.LastIndexOf('/', normalizedUpTo - 2)));
                    }
                    else
                    {
                        normalized = Substring(path, 0, normalizedUpTo);
                    }
                }
                else
                {
                    var lastSlash = normalized.LastIndexOf('/');
                    if (lastSlash != -1)
                    {
                        normalized = Substring(normalized, 0, Math.Max(rootLength, lastSlash));
                    }
                    else
                    {
                        normalized = root;
                    }
                    if (normalized.Length == rootLength)
                    {
                        seenNonDotDotSegment = rootLength != 0;
                    }
                }
            }
            else if (normalized is not null)
            {
                if (normalized.Length != rootLength)
                {
                    normalized += DirectorySeparator;
                }
                seenNonDotDotSegment = true;
                normalized += Substring(path, segmentStart, segmentEnd);
            }
            else
            {
                seenNonDotDotSegment = true;
                normalizedUpTo = segmentEnd;
            }
            index = segmentEnd + 1;
        }
        return normalized ?? (length > rootLength ? RemoveTrailingDirectorySeparator(path) : path);
    }

    // path.ts:722
    internal static string NormalizePath(string path)
    {
        path = NormalizeSlashes(path);
        var normalized = SimpleNormalizePath(path);
        if (normalized is not null)
        {
            return normalized;
        }
        var absolute = GetNormalizedAbsolutePath(path, "");
        return absolute.Length > 0 && HasTrailingDirectorySeparator(path) ? EnsureTrailingDirectorySeparator(absolute) : absolute;
    }

    // path.ts:732
    private static string? SimpleNormalizePath(string path)
    {
        // Most paths don't require normalization
        if (!RelativePathSegmentRegExpTest(path))
        {
            return path;
        }
        // Some paths only require cleanup of `/./` or leading `./`
        var simplified = path.Replace("/./", "/", StringComparison.Ordinal);
        if (simplified.StartsWith("./", StringComparison.Ordinal))
        {
            simplified = simplified.Substring(2);
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

    /// <summary>Removes a trailing directory separator from a path, if it does not already have one.</summary>
    // path.ts:781
    internal static string RemoveTrailingDirectorySeparator(string path)
    {
        if (HasTrailingDirectorySeparator(path))
        {
            return path.Substring(0, path.Length - 1);
        }

        return path;
    }

    /// <summary>Adds a trailing directory separator to a path, if it does not already have one.</summary>
    // path.ts:803
    internal static string EnsureTrailingDirectorySeparator(string path)
    {
        if (!HasTrailingDirectorySeparator(path))
        {
            return path + DirectorySeparator;
        }

        return path;
    }

    // path.ts:882 `relativePathSegmentRegExp = /\/\/|(?:^|\/)\.\.?(?:$|\/)/`, checks a path for these segments: '', '.'. '..'.
    // The scanner and parser use no regular expressions (Gate 2), so the pattern is matched by hand:
    // `//` anywhere, or a `.` or `..` that starts the path or follows a `/` and ends the path or precedes a `/`.
    private static bool RelativePathSegmentRegExpTest(string path)
    {
        var length = path.Length;
        for (var i = 0; i < length; i++)
        {
            var ch = path[i];
            if (ch == '/')
            {
                if (i + 1 < length && path[i + 1] == '/')
                {
                    return true;
                }
            }
            else if (ch == '.' && (i == 0 || path[i - 1] == '/'))
            {
                var next = i + 1;
                if (next == length || path[next] == '/')
                {
                    return true;
                }
                if (path[next] == '.' && (next + 1 == length || path[next + 1] == '/'))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // utilities.ts:10170
    private static readonly string[] extensionsToRemove = [Extension.Dts, Extension.Dmts, Extension.Dcts, Extension.Mjs, Extension.Mts, Extension.Cjs, Extension.Cts, Extension.Ts, Extension.Js, Extension.Tsx, Extension.Jsx, Extension.Json];

    // utilities.ts:10302 (its C# home is TsPath: the gate of module M1 calls it there)
    internal static string? TryGetExtensionFromPath(string path)
    {
        return Core.Find(extensionsToRemove, (e, _) => FileExtensionIs(path, e));
    }
}
