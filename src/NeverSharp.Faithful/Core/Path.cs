// Copyright 2026 Andre Kaufmann
// Portions Copyright (c) Microsoft Corporation.
// SPDX-License-Identifier: Apache-2.0
//
// Ported from src/compiler/path.ts
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Ported from src/compiler/utilities.ts (tryGetExtensionFromPath with one argument, extensionsToRemove)
//   https://github.com/microsoft/TypeScript at commit 050880ce59e30b356b686bd3144efe24f875ebc8
// Port structure follows internal/tspath/path.go and internal/tspath/extension.go
//   https://github.com/microsoft/typescript-go at commit 2bd066d87f5bafd315be9f40889d0a60b9e58e0b
// Modified: translated from TypeScript to C#.

using NeverSharp.Generated;

namespace NeverSharp.Faithful;

/// <summary>
/// path.ts. Single-character separator strings ("/", "\") are searched as <c>char</c>, as typescript-go
/// searches bytes (internal/tspath/path.go, strings.IndexByte). Character codes are <c>int</c>; JavaScript's <c>charCodeAt</c> returns <c>NaN</c> past the end
/// of the string, which compares unequal to every character code: <see cref="CharCodeAt"/> returns -1 there,
/// which equals none of the codes compared in this file.
/// </summary>
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

    //// Path Tests

    /// <summary>Determines whether a charCode corresponds to <c>/</c> or <c>\</c>.</summary>
    internal static bool IsAnyDirectorySeparator(int charCode)
    {
        return charCode == (int)CharacterCodes.slash || charCode == (int)CharacterCodes.backslash;
    }

    internal static bool FileExtensionIs(string path, string extension)
    {
        return path.Length > extension.Length && Core.EndsWith(path, extension);
    }

    /// <summary>Determines whether a path has a trailing separator (<c>/</c> or <c>\\</c>).</summary>
    internal static bool HasTrailingDirectorySeparator(string path)
    {
        return path.Length > 0 && IsAnyDirectorySeparator(path[path.Length - 1]);
    }

    //// Path Parsing

    private static bool IsVolumeCharacter(int charCode)
    {
        return (charCode >= (int)CharacterCodes.a && charCode <= (int)CharacterCodes.z) ||
            (charCode >= (int)CharacterCodes.A && charCode <= (int)CharacterCodes.Z);
    }

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

            var p1 = path.IndexOf(ch0 == (int)CharacterCodes.slash ? '/' : '\\', 2);
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
                    IsVolumeCharacter(CharCodeAt(path, authorityEnd + 1)))
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
    /// Semantics align with NodeJS's <c>path.basename</c> except that we support URL's as well.
    /// <code>
    /// getBaseFileName("/path/to/file.ext") === "file.ext"
    /// getBaseFileName("/path/to/") === "to"
    /// getBaseFileName("/") === ""
    /// </code>
    /// </summary>
    internal static string GetBaseFileName(string path)
    {
        return GetBaseFileName(path, null, null, null);
    }

    /// <summary>
    /// Gets the portion of a path following the last (non-terminal) separator (<c>/</c>).
    /// Semantics align with NodeJS's <c>path.basename</c> except that we support URL's as well.
    /// If the base name has any one of the provided extensions, it is removed.
    /// </summary>
    internal static string GetBaseFileName(string path, string extensions, bool ignoreCase)
    {
        return GetBaseFileName(path, null, extensions, ignoreCase);
    }

    /// <summary>path.ts: <c>getBaseFileName(path, extensions: string | readonly string[], ignoreCase)</c>.</summary>
    internal static string GetBaseFileName(string path, IReadOnlyList<string> extensions, bool ignoreCase)
    {
        return GetBaseFileName(path, extensions, null, ignoreCase);
    }

    // The implementation signature: `extensions?: string | readonly string[]` is one of extensionList and extensionString.
    private static string GetBaseFileName(string path, IReadOnlyList<string>? extensionList, string? extensionString, bool? ignoreCase)
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
        var name = path.Substring(Math.Max(GetRootLength(path), path.LastIndexOf('/') + 1));
        var extension = (extensionList is not null || extensionString is not null) && ignoreCase is not null
            ? extensionList is not null ? GetAnyExtensionFromPath(name, extensionList, ignoreCase.Value) : GetAnyExtensionFromPath(name, extensionString!, ignoreCase.Value)
            : null;
        return !string.IsNullOrEmpty(extension) ? name.Substring(0, name.Length - extension.Length) : name;
    }

    private static string? TryGetExtensionFromPath(string path, string extension, Func<string, string, bool> stringEqualityComparer)
    {
        if (!Core.StartsWith(extension, "."))
        {
            extension = "." + extension;
        }
        if (path.Length >= extension.Length && path[path.Length - extension.Length] == (int)CharacterCodes.dot)
        {
            var pathExtension = path.Substring(path.Length - extension.Length);
            if (stringEqualityComparer(pathExtension, extension))
            {
                return pathExtension;
            }
        }
        return null;
    }

    private static string GetAnyExtensionFromPathWorker(string path, IReadOnlyList<string> extensions, Func<string, string, bool> stringEqualityComparer)
    {
        foreach (var extension in extensions)
        {
            var result = TryGetExtensionFromPath(path, extension, stringEqualityComparer);
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

    /// <summary>
    /// Gets the file extension for a path, provided it is the provided extension
    /// (path.ts: <c>extensions: string</c>; the empty string is falsy and gives the plain extension).
    /// <code>
    /// getAnyExtensionFromPath("/path/to/file.ext", ".ext", true) === ".ext"
    /// getAnyExtensionFromPath("/path/to/file.js", ".ext", true) === ""
    /// getAnyExtensionFromPath("/path/to/file.ext", ".EXT", false) === ""
    /// </code>
    /// </summary>
    internal static string GetAnyExtensionFromPath(string path, string extensions, bool ignoreCase)
    {
        if (!string.IsNullOrEmpty(extensions))
        {
            return TryGetExtensionFromPath(RemoveTrailingDirectorySeparator(path), extensions, ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive) ?? "";
        }
        return GetAnyExtensionFromPath(path);
    }

    /// <summary>
    /// Gets the file extension for a path, provided it is one of the provided extensions.
    /// <code>
    /// getAnyExtensionFromPath("/path/to/file.js", [".ext", ".js"], true) === ".js"
    /// </code>
    /// </summary>
    internal static string GetAnyExtensionFromPath(string path, IReadOnlyList<string> extensions, bool ignoreCase)
    {
        // An array is truthy even when empty.
        return GetAnyExtensionFromPathWorker(RemoveTrailingDirectorySeparator(path), extensions, ignoreCase ? Core.EquateStringsCaseInsensitive : Core.EquateStringsCaseSensitive);
    }

    /// <summary>Normalize path separators, converting <c>\</c> into <c>/</c>.</summary>
    internal static string NormalizeSlashes(string path)
    {
        return path.Contains('\\')
            ? BackslashRegExp.Replace(path, DirectorySeparator)
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
        if (simpleNormalized is not null)
        {
            return simpleNormalized.Length > rootLength ? RemoveTrailingDirectorySeparator(simpleNormalized) : simpleNormalized;
        }

        var length = path.Length;
        var root = path.Substring(0, rootLength);
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
            int ch = path[index];
            while (ch == (int)CharacterCodes.slash && index + 1 < length)
            {
                index++;
                ch = path[index];
            }
            if (index > segmentStart)
            {
                // Seen superfluous separator
                normalized ??= JsSubstring(path, 0, segmentStart - 1);
                segmentStart = index;
            }
            // Past any superfluous separators
            var segmentEnd = path.IndexOf('/', index + 1);
            if (segmentEnd == -1)
            {
                segmentEnd = length;
            }
            var segmentLength = segmentEnd - segmentStart;
            if (segmentLength == 1 && path[index] == (int)CharacterCodes.dot)
            {
                // "." segment (skip)
                normalized ??= path.Substring(0, normalizedUpTo);
            }
            else if (segmentLength == 2 && path[index] == (int)CharacterCodes.dot && path[index + 1] == (int)CharacterCodes.dot)
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
                        normalized = JsSubstring(path, 0, Math.Max(rootLength, path.LastIndexOf('/', normalizedUpTo - 2)));
                    }
                    else
                    {
                        normalized = path.Substring(0, normalizedUpTo);
                    }
                }
                else
                {
                    var lastSlash = normalized.LastIndexOf('/');
                    if (lastSlash != -1)
                    {
                        normalized = normalized.Substring(0, Math.Max(rootLength, lastSlash));
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
                normalized += path.Substring(segmentStart, segmentEnd - segmentStart);
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

    internal static string NormalizePath(string path)
    {
        path = NormalizeSlashes(path);
        var normalized = SimpleNormalizePath(path);
        if (normalized is not null)
        {
            return normalized;
        }
        normalized = GetNormalizedAbsolutePath(path, "");
        return !string.IsNullOrEmpty(normalized) && HasTrailingDirectorySeparator(path) ? EnsureTrailingDirectorySeparator(normalized) : normalized;
    }

    private static string? SimpleNormalizePath(string path)
    {
        // Most paths don't require normalization
        if (!RelativePathSegmentRegExp.Test(path))
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
            if (!RelativePathSegmentRegExp.Test(path))
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
            return path.Substring(0, path.Length - 1);
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

    //// Extensions (utilities.ts)

    // utilities.ts: `const extensionsToRemove = [Extension.Dts, Extension.Dmts, Extension.Dcts, Extension.Mjs, Extension.Mts,
    // Extension.Cjs, Extension.Cts, Extension.Ts, Extension.Js, Extension.Tsx, Extension.Jsx, Extension.Json];`
    private static readonly string[] ExtensionsToRemove = [".d.ts", ".d.mts", ".d.cts", ".mjs", ".mts", ".cjs", ".cts", ".ts", ".js", ".tsx", ".jsx", ".json"];

    /// <summary>
    /// utilities.ts: <c>tryGetExtensionFromPath(path): Extension | undefined</c>, the exported function of that name
    /// (path.ts's three-argument function is file-local). It lives here because the C# API places it in TsPath.
    /// </summary>
    internal static string? TryGetExtensionFromPath(string path)
    {
        return Core.Find(ExtensionsToRemove, (e, _) => FileExtensionIs(path, e));
    }

    //// Path Comparisons

    /// <summary>
    /// path.ts: <c>const relativePathSegmentRegExp = /\/\/|(?:^|\/)\.\.?(?:$|\/)/;</c> (check path for these segments: '', '.'. '..').
    /// Regular expressions are not used in the port (eng/BannedSymbols.Faithful.txt); <see cref="Test"/> matches
    /// exactly what the expression matches, as typescript-go's hasRelativePathSegment (internal/tspath/path.go) does.
    /// </summary>
    private static class RelativePathSegmentRegExp
    {
        internal static bool Test(string path)
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
                    // `(?:^|\/)\.\.?(?:$|\/)`
                    var next = i + 1;
                    if (next == length || path[next] == (int)CharacterCodes.slash)
                    {
                        return true;
                    }
                    if (path[next] == (int)CharacterCodes.dot && (next + 1 == length || path[next + 1] == (int)CharacterCodes.slash))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>
    /// path.ts: <c>const backslashRegExp = /\\/g;</c>, used as <c>path.replace(backslashRegExp, directorySeparator)</c>.
    /// Regular expressions are not used in the port; <see cref="Replace"/> replaces every backslash, as
    /// typescript-go's NormalizeSlashes does with strings.ReplaceAll (internal/tspath/path.go).
    /// </summary>
    private static class BackslashRegExp
    {
        internal static string Replace(string input, string replacement)
        {
            return input.Replace(AltDirectorySeparator, replacement, StringComparison.Ordinal);
        }
    }

    // String.prototype.charCodeAt: NaN past the end (here -1, see the class summary).
    private static int CharCodeAt(string text, int index)
    {
        return (uint)index < (uint)text.Length ? text[index] : -1;
    }

    // String.prototype.substring(start, end): clamps both offsets to [0, length] and swaps them if start > end.
    private static string JsSubstring(string text, int start, int end)
    {
        var from = Math.Min(Math.Max(start, 0), text.Length);
        var to = Math.Min(Math.Max(end, 0), text.Length);
        if (from > to)
        {
            (from, to) = (to, from);
        }
        return text.Substring(from, to - from);
    }
}
