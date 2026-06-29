// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;

namespace Microsoft.Agents.AI;

/// <summary>
/// Internal helpers shared by <see cref="FileAccessProvider"/> and <see cref="FileMemoryProvider"/>
/// for the <c>replace</c> and <c>replace_lines</c> tools.
/// </summary>
internal static class FileEditor
{
    /// <summary>
    /// Replaces occurrences of <paramref name="oldString"/> with <paramref name="newString"/> in
    /// <paramref name="content"/>, returning the new content and the number of replacements made.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="oldString"/> is empty, is not found, or occurs more than once
    /// while <paramref name="replaceAll"/> is <see langword="false"/>.
    /// </exception>
    internal static (string Content, int Count) ApplyReplace(string content, string oldString, string newString, bool replaceAll)
    {
        if (string.IsNullOrEmpty(oldString))
        {
            throw new ArgumentException("old_string must not be empty.");
        }

        int count = CountOccurrences(content, oldString);
        if (count == 0)
        {
            throw new ArgumentException($"old_string not found: '{oldString}'.");
        }

        if (count > 1 && !replaceAll)
        {
            throw new ArgumentException(
                $"old_string occurs {count} times; pass replace_all=true to replace all, " +
                "or provide a more specific old_string.");
        }

#if NET8_0_OR_GREATER
        return (content.Replace(oldString, newString, StringComparison.Ordinal), count);
#else
        return (content.Replace(oldString, newString), count);
#endif
    }

    /// <summary>
    /// Applies whole-line (1-based) replacements to <paramref name="content"/>, preserving a trailing
    /// newline if the original had one.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="edits"/> is empty, any line number is out of range, or a line number
    /// is targeted more than once.
    /// </exception>
    internal static string ApplyReplaceLines(string content, IReadOnlyList<FileLineEdit> edits)
    {
        if (edits.Count == 0)
        {
            throw new ArgumentException("At least one line edit must be provided.");
        }

        bool hadTrailingNewline = content.EndsWith("\n", StringComparison.Ordinal);
        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = SplitLines(content);

        var seen = new HashSet<int>();
        foreach (FileLineEdit edit in edits)
        {
            if (!seen.Add(edit.LineNumber))
            {
                throw new ArgumentException($"Duplicate line_number {edit.LineNumber} in edits.");
            }

            if (edit.LineNumber < 1 || edit.LineNumber > lines.Count)
            {
                throw new ArgumentException(
                    $"line_number {edit.LineNumber} is out of range (file has {lines.Count} lines).");
            }
        }

        foreach (FileLineEdit edit in edits)
        {
            lines[edit.LineNumber - 1] = edit.NewLine;
        }

        string result = string.Join(newline, lines);
        return hadTrailingNewline ? result + newline : result;
    }

    private static int CountOccurrences(string content, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    /// <summary>
    /// Splits content into lines. A trailing <c>\r</c> is stripped from each line and the empty segment produced by a final newline
    /// is not treated as a line.
    /// </summary>
    private static List<string> SplitLines(string content)
    {
        var lines = new List<string>();
        if (content.Length == 0)
        {
            return lines;
        }

        string[] parts = content.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            // The final empty segment after a trailing '\n' is not a line.
            if (i == parts.Length - 1 && parts[i].Length == 0)
            {
                break;
            }

            string line = parts[i];
            if (line.EndsWith("\r", StringComparison.Ordinal))
            {
                line = line.Substring(0, line.Length - 1);
            }

            lines.Add(line);
        }

        return lines;
    }
}
