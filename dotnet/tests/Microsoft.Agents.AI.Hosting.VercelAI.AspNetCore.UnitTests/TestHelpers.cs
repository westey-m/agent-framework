// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.UnitTests;

internal static class TestHelpers
{
    /// <summary>
    /// Converts a synchronous <see cref="IEnumerable{T}"/> into an <see cref="IAsyncEnumerable{T}"/>
    /// for use in tests that exercise async streaming pipelines.
    /// </summary>
    internal static async IAsyncEnumerable<T> ToAsyncEnumerableAsync<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
    }
}
