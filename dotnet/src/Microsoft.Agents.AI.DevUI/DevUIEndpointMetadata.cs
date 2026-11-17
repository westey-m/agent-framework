// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Endpoint metadata that marks an endpoint as part of the DevUI surface.
/// </summary>
internal sealed class DevUIEndpointMetadata
{
    /// <summary>
    /// Gets the shared marker instance.
    /// </summary>
    public static DevUIEndpointMetadata Instance { get; } = new();

    private DevUIEndpointMetadata()
    {
    }
}
