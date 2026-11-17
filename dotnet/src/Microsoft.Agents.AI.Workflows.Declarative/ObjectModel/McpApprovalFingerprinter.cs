// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Microsoft.Agents.AI.Workflows.Declarative.ObjectModel;

internal sealed class McpApprovalFingerprinter(byte[]? headerFingerprintKey = null)
{
    private static readonly byte[] s_processHeaderFingerprintKey = CreateProcessHeaderFingerprintKey();

    private readonly byte[] _headerFingerprintKey = (byte[])(headerFingerprintKey ?? s_processHeaderFingerprintKey).Clone();

    public byte[] ComputeHeaderFingerprint(string executorId, string requestId, Dictionary<string, string>? headers, bool keyIdentifier)
    {
        // Bind the digest to this request and normalize header names and ordering for stable comparisons.
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(keyIdentifier ? "mcp-header-fingerprint-key-v1" : "mcp-header-fingerprint-v1");
            writer.WriteStringValue(executorId);
            writer.WriteStringValue(requestId);
            if (!keyIdentifier)
            {
                if (headers is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStartArray();
                    foreach (KeyValuePair<string, string> header in headers
                        .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(header => header.Key, StringComparer.Ordinal))
                    {
                        writer.WriteStartArray();
                        writer.WriteStringValue(header.Key.ToUpperInvariant());
                        writer.WriteStringValue(header.Value);
                        writer.WriteEndArray();
                    }

                    writer.WriteEndArray();
                }
            }

            writer.WriteEndArray();
        }

        using HMACSHA256 hmac = new(this._headerFingerprintKey);
        return hmac.ComputeHash(stream.ToArray());
    }

    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        // Compare every byte so a matching prefix does not shorten the comparison.
        int difference = 0;
        for (int index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }

        return difference == 0;
    }

    public static string ComputeToolDefinitionFingerprint(JsonElement tool)
    {
        // Include the complete published definition so changes to annotations and other metadata invalidate approval.
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            WriteCanonicalJson(writer, tool);
        }

#if NET
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
#else
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream.ToArray())).Replace("-", string.Empty);
#endif
    }

    private static byte[] CreateProcessHeaderFingerprintKey()
    {
        // The default supports restores within this process; hosts can provide a stable key for cross-process restores.
        byte[] key = new byte[32];
        using RandomNumberGenerator random = RandomNumberGenerator.Create();
        random.GetBytes(key);
        return key;
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                // Object property order is immaterial, while array order remains part of the definition.
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement element in value.EnumerateArray())
                {
                    WriteCanonicalJson(writer, element);
                }

                writer.WriteEndArray();
                break;

            default:
                value.WriteTo(writer);
                break;
        }
    }
}
