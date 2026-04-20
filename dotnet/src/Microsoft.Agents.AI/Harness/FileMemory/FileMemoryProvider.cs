// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// An <see cref="AIContextProvider"/> that provides file-based memory tools to an agent
/// for storing, retrieving, modifying, listing, deleting, and searching files.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="FileMemoryProvider"/> enables agents to persist information across interactions
/// using a file-based storage model. Each memory is stored as an individual file with a meaningful name.
/// For large files, a companion description file (suffixed with <c>_description.md</c>) can be stored
/// alongside the main file to provide a summary.
/// </para>
/// <para>
/// File access is mediated through a <see cref="AgentFileStore"/> abstraction, allowing pluggable
/// backends (in-memory, local file system, remote blob storage, etc.).
/// </para>
/// <para>
/// This provider exposes the following tools to the agent:
/// <list type="bullet">
/// <item><description><c>SaveFile</c> — Save a memory file with the given name, content, and an optional description.</description></item>
/// <item><description><c>ReadFile</c> — Read the content of a file by name.</description></item>
/// <item><description><c>DeleteFile</c> — Delete a file by name.</description></item>
/// <item><description><c>ListFiles</c> — List all files with their descriptions (if available).</description></item>
/// <item><description><c>SearchFiles</c> — Search file contents using a regular expression pattern.</description></item>
/// </list>
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class FileMemoryProvider : AIContextProvider
{
    private const string DescriptionSuffix = "_description.md";

    private const string DefaultInstructions =
        """
        You have access to a file-based memory system via the FileMemory_* tools for storing and retrieving information across interactions.
        Use FileMemory_SaveFile to store one memory per file with a clear, descriptive file name (e.g., "projectarchitecture.md", "userpreferences.md").
        For large files, include a description when saving to provide a summary that helps with discovery.
        Before starting new tasks, use FileMemory_ListFiles and FileMemory_SearchFiles to check for relevant existing memories.
        Use FileMemory_ReadFile to retrieve file contents and FileMemory_DeleteFile to remove outdated memories.
        Keep memories up-to-date by overwriting files when information changes.
        When you receive large amounts of data (e.g., downloaded web pages, API responses, research results),
        save them to files if they will be required later, so that they are not lost when older context is compacted or truncated.
        This ensures important data remains accessible across long-running sessions.
        """;

    private readonly AgentFileStore _fileStore;
    private readonly ProviderSessionState<FileMemoryState> _sessionState;
    private IReadOnlyList<string>? _stateKeys;
    private AITool[]? _tools;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileMemoryProvider"/> class.
    /// </summary>
    /// <param name="fileStore">The file store implementation used for storage operations.</param>
    /// <param name="stateInitializer">
    /// An optional function that initializes the <see cref="FileMemoryState"/> for a new session.
    /// Use this to customize the working folder (e.g., per-user or per-session subfolders).
    /// When <see langword="null"/>, the default initializer creates state with an empty working folder.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStore"/> is <see langword="null"/>.</exception>
    public FileMemoryProvider(AgentFileStore fileStore, Func<AgentSession?, FileMemoryState>? stateInitializer = null)
    {
        Throw.IfNull(fileStore);

        this._fileStore = fileStore;
        this._sessionState = new ProviderSessionState<FileMemoryState>(
            stateInitializer ?? (_ => new FileMemoryState()),
            this.GetType().Name,
            AgentJsonUtilities.DefaultOptions);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => this._stateKeys ??= [this._sessionState.StateKey];

    /// <inheritdoc />
    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        FileMemoryState state = this._sessionState.GetOrInitializeState(context.Session);

        // Ensure the working folder exists in the store.
        if (!string.IsNullOrEmpty(state.WorkingFolder))
        {
            await this._fileStore.CreateDirectoryAsync(state.WorkingFolder, cancellationToken).ConfigureAwait(false);
        }

        return new AIContext
        {
            Instructions = DefaultInstructions,
            Tools = this._tools ??= this.CreateTools(),
        };
    }

    /// <summary>
    /// Save a memory file with the given name and content.
    /// Overwrites the file if it already exists.
    /// Include a description for large files to provide a summary that helps with discovery.
    /// </summary>
    /// <param name="fileName">The name of the file to save.</param>
    /// <param name="content">The content to write to the file.</param>
    /// <param name="description">An optional description of the file contents for discovery. Leave empty or omit to skip.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A confirmation message.</returns>
    [Description("Save a memory file with the given name and content. Overwrites the file if it already exists. Include a description for large files to provide a summary that helps with discovery.")]
    private async Task<string> SaveFileAsync(string fileName, string content, string? description = null, CancellationToken cancellationToken = default)
    {
        FileMemoryState state = this._sessionState.GetOrInitializeState(AIAgent.CurrentRunContext?.Session);
        string path = ResolvePath(state.WorkingFolder, fileName);
        await this._fileStore.WriteFileAsync(path, content, cancellationToken).ConfigureAwait(false);

        string descPath = ResolvePath(state.WorkingFolder, GetDescriptionFileName(fileName));

        if (!string.IsNullOrWhiteSpace(description))
        {
            await this._fileStore.WriteFileAsync(descPath, description, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Remove any stale description file when no description is provided.
            await this._fileStore.DeleteFileAsync(descPath, cancellationToken).ConfigureAwait(false);
        }

        return string.IsNullOrWhiteSpace(description)
            ? $"File '{fileName}' saved."
            : $"File '{fileName}' saved with description.";
    }

    /// <summary>
    /// Read the content of a memory file by name.
    /// Returns the file content or a message indicating the file was not found.
    /// </summary>
    /// <param name="fileName">The name of the file to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file content or a not-found message.</returns>
    [Description("Read the content of a memory file by name. Returns the file content or a message indicating the file was not found.")]
    private async Task<string> ReadFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        FileMemoryState state = this._sessionState.GetOrInitializeState(AIAgent.CurrentRunContext?.Session);
        string path = ResolvePath(state.WorkingFolder, fileName);
        string? content = await this._fileStore.ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        return content ?? $"File '{fileName}' not found.";
    }

    /// <summary>
    /// Delete a memory file by name. Also removes its companion description file if one exists.
    /// </summary>
    /// <param name="fileName">The name of the file to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A confirmation or not-found message.</returns>
    [Description("Delete a memory file by name. Also removes its companion description file if one exists.")]
    private async Task<string> DeleteFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        FileMemoryState state = this._sessionState.GetOrInitializeState(AIAgent.CurrentRunContext?.Session);
        string path = ResolvePath(state.WorkingFolder, fileName);
        bool deleted = await this._fileStore.DeleteFileAsync(path, cancellationToken).ConfigureAwait(false);

        // Also delete companion description file if it exists.
        string descPath = ResolvePath(state.WorkingFolder, GetDescriptionFileName(fileName));
        await this._fileStore.DeleteFileAsync(descPath, cancellationToken).ConfigureAwait(false);

        return deleted ? $"File '{fileName}' deleted." : $"File '{fileName}' not found.";
    }

    /// <summary>
    /// List all memory files with their descriptions (if available). Description files are not shown separately.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of file entries with names and optional descriptions.</returns>
    [Description("List all memory files with their descriptions (if available). Description files are not shown separately.")]
    private async Task<List<FileListEntry>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        FileMemoryState state = this._sessionState.GetOrInitializeState(AIAgent.CurrentRunContext?.Session);
        IReadOnlyList<string> fileNames = await this._fileStore.ListFilesAsync(state.WorkingFolder, cancellationToken).ConfigureAwait(false);

        var descriptionFileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in fileNames)
        {
            if (file.EndsWith(DescriptionSuffix, StringComparison.OrdinalIgnoreCase))
            {
                descriptionFileSet.Add(file);
            }
        }

        var entries = new List<FileListEntry>();
        foreach (string file in fileNames)
        {
            if (descriptionFileSet.Contains(file))
            {
                continue;
            }

            string? fileDescription = null;
            string descFileName = GetDescriptionFileName(file);

            if (descriptionFileSet.Contains(descFileName))
            {
                string descPath = CombinePaths(state.WorkingFolder, descFileName);
                fileDescription = await this._fileStore.ReadFileAsync(descPath, cancellationToken).ConfigureAwait(false);
            }

            entries.Add(new FileListEntry { FileName = file, Description = fileDescription });
        }

        return entries;
    }

    /// <summary>
    /// Search memory file contents using a regular expression pattern (case-insensitive).
    /// Optionally filter which files to search using a glob pattern.
    /// Returns matching file names, content snippets, and matching lines with line numbers.
    /// </summary>
    /// <param name="regexPattern">A regular expression pattern to match against file contents (case-insensitive).</param>
    /// <param name="filePattern">An optional glob pattern to filter which files to search (e.g., "*.md", "research*"). Leave empty or omit to search all files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of search results with matching file names, snippets, and matching lines.</returns>
    [Description("Search memory file contents using a regular expression pattern (case-insensitive). Optionally filter which files to search using a glob pattern (e.g., \"*.md\", \"research*\"). Returns matching file names, content snippets, and matching lines with line numbers.")]
    private async Task<List<FileSearchResult>> SearchFilesAsync(string regexPattern, string? filePattern = null, CancellationToken cancellationToken = default)
    {
        FileMemoryState state = this._sessionState.GetOrInitializeState(AIAgent.CurrentRunContext?.Session);
        string? pattern = string.IsNullOrWhiteSpace(filePattern) ? null : filePattern;
        IReadOnlyList<FileSearchResult> results = await this._fileStore.SearchFilesAsync(state.WorkingFolder, regexPattern, pattern, cancellationToken).ConfigureAwait(false);
        return new List<FileSearchResult>(results);
    }

    private AITool[] CreateTools()
    {
        var serializerOptions = AgentJsonUtilities.DefaultOptions;

        return
        [
            AIFunctionFactory.Create(this.SaveFileAsync, new AIFunctionFactoryOptions { Name = "FileMemory_SaveFile", SerializerOptions = serializerOptions }),
            AIFunctionFactory.Create(this.ReadFileAsync, new AIFunctionFactoryOptions { Name = "FileMemory_ReadFile", SerializerOptions = serializerOptions }),
            AIFunctionFactory.Create(this.DeleteFileAsync, new AIFunctionFactoryOptions { Name = "FileMemory_DeleteFile", SerializerOptions = serializerOptions }),
            AIFunctionFactory.Create(this.ListFilesAsync, new AIFunctionFactoryOptions { Name = "FileMemory_ListFiles", SerializerOptions = serializerOptions }),
            AIFunctionFactory.Create(this.SearchFilesAsync, new AIFunctionFactoryOptions { Name = "FileMemory_SearchFiles", SerializerOptions = serializerOptions }),
        ];
    }

    private static string GetDescriptionFileName(string fileName)
    {
        int extIndex = fileName.LastIndexOf('.');
        if (extIndex > 0)
        {
#pragma warning disable CA1845 // Use span-based 'string.Concat' — not available on all target frameworks
            return fileName.Substring(0, extIndex) + DescriptionSuffix;
#pragma warning restore CA1845
        }

        return fileName + DescriptionSuffix;
    }

    private static string ResolvePath(string workingFolder, string fileName)
    {
        // Prevent path traversal by rejecting rooted paths and '.'/'..' segments.
        string normalized = fileName.Replace('\\', '/');

        if (Path.IsPathRooted(fileName) ||
            fileName.StartsWith("/", StringComparison.Ordinal) ||
            fileName.StartsWith("\\", StringComparison.Ordinal) ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            throw new ArgumentException($"Invalid file name: '{fileName}'. File names must be relative and must not start with '/', '\\', or a drive root.", nameof(fileName));
        }

        foreach (string segment in normalized.Split('/'))
        {
            if (segment.Equals(".", StringComparison.Ordinal) || segment.Equals("..", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Invalid file name: '{fileName}'. File names must not contain '.' or '..' segments.", nameof(fileName));
            }
        }

        return CombinePaths(workingFolder, fileName);
    }

    private static string CombinePaths(string basePath, string relativePath)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return relativePath;
        }

        if (string.IsNullOrEmpty(relativePath))
        {
            return basePath;
        }

        return basePath.TrimEnd('/') + "/" + relativePath.TrimStart('/');
    }
}
