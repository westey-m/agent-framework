// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides an abstract base class for file storage operations.
/// </summary>
/// <remarks>
/// <para>
/// All paths are relative to an implementation-defined root. Implementations may map these
/// paths to a local file system, in-memory store, remote blob storage, or other mechanisms.
/// </para>
/// <para>
/// Paths use forward slashes as separators and must not escape the root (e.g., via <c>..</c> segments).
/// It is up to each implementation to ensure that this is enforced.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public abstract class AgentFileStore
{
    /// <summary>
    /// Writes content to a file, creating or overwriting it.
    /// </summary>
    /// <param name="path">The relative path of the file to write.</param>
    /// <param name="content">The content to write to the file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the content of a file.
    /// </summary>
    /// <param name="path">The relative path of the file to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file content, or <see langword="null"/> if the file does not exist.</returns>
    public abstract Task<string?> ReadFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">The relative path of the file to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the file was deleted; <see langword="false"/> if it did not exist.</returns>
    public abstract Task<bool> DeleteFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists files in a directory.
    /// </summary>
    /// <param name="directory">The relative path of the directory to list. Use an empty string for the root.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of file names in the specified directory (direct children only).</returns>
    public abstract Task<IReadOnlyList<string>> ListFilesAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a file exists.
    /// </summary>
    /// <param name="path">The relative path of the file to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the file exists; otherwise, <see langword="false"/>.</returns>
    public abstract Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for files whose content matches a regular expression pattern.
    /// </summary>
    /// <param name="directory">The relative path of the directory to search. Use an empty string for the root.</param>
    /// <param name="regexPattern">
    /// A regular expression pattern to match against file contents. The pattern is matched case-insensitively.
    /// For example, <c>"error|warning"</c> matches lines containing "error" or "warning".
    /// </param>
    /// <param name="filePattern">
    /// An optional glob pattern to filter which files are searched (e.g., <c>"*.md"</c>, <c>"research*"</c>).
    /// When <see langword="null"/>, all files in the directory are searched.
    /// Uses standard glob syntax from <see cref="Matcher"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of search results with matching file names, snippets, and matching lines.</returns>
    public abstract Task<IReadOnlyList<FileSearchResult>> SearchFilesAsync(string directory, string regexPattern, string? filePattern = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a directory exists, creating it if necessary.
    /// </summary>
    /// <param name="path">The relative path of the directory to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes a relative path by replacing backslashes with forward slashes, trimming leading
    /// and trailing separators, and collapsing consecutive separators. Also validates that the path
    /// does not contain rooted paths, drive roots, or <c>.</c>/<c>..</c> traversal segments.
    /// </summary>
    /// <param name="path">The relative path to normalize.</param>
    /// <returns>The normalized forward-slash path.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is rooted, starts with a drive letter, or contains
    /// <c>.</c> or <c>..</c> segments.
    /// </exception>
    protected static string NormalizeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/').Trim('/');

        if (Path.IsPathRooted(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("\\", StringComparison.Ordinal) ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            throw new ArgumentException(
                $"Invalid path: '{path}'. Paths must be relative and must not start with '/', '\\', or a drive root.",
                nameof(path));
        }

        // Split, validate segments, and filter out empty segments to collapse consecutive separators.
        string[] segments = normalized.Split('/');
        var cleanSegments = new List<string>(segments.Length);
        foreach (string segment in segments)
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (segment.Equals(".", StringComparison.Ordinal) || segment.Equals("..", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Invalid path: '{path}'. Paths must not contain '.' or '..' segments.",
                    nameof(path));
            }

            cleanSegments.Add(segment);
        }

        return string.Join("/", cleanSegments);
    }

    /// <summary>
    /// Creates a <see cref="Matcher"/> for the specified glob pattern. Use the returned instance
    /// to test multiple file names without allocating a new matcher for each one.
    /// </summary>
    /// <param name="filePattern">
    /// The glob pattern to match against (e.g., <c>"*.md"</c>, <c>"research*"</c>).
    /// </param>
    /// <returns>A <see cref="Matcher"/> configured with the specified pattern.</returns>
    protected static Matcher CreateGlobMatcher(string filePattern)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(filePattern);
        return matcher;
    }

    /// <summary>
    /// Determines whether a file name matches a pre-built glob <see cref="Matcher"/>.
    /// </summary>
    /// <param name="fileName">The file name to test (not a full path — just the name).</param>
    /// <param name="matcher">
    /// A pre-built <see cref="Matcher"/> to test against.
    /// When <see langword="null"/>, this method returns <see langword="true"/> for any file name.
    /// </param>
    /// <returns><see langword="true"/> if the file name matches the pattern or if the matcher is <see langword="null"/>; otherwise, <see langword="false"/>.</returns>
    protected static bool MatchesGlob(string fileName, Matcher? matcher)
    {
        if (matcher is null)
        {
            return true;
        }

        PatternMatchingResult result = matcher.Match(fileName);
        return result.HasMatches;
    }
}
