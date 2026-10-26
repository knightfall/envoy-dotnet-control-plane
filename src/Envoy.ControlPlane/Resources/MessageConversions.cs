// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

namespace Envoy.ControlPlane.Resources;

/// <summary>Converts protobuf object messages and Struct through the standard protobuf JSON representation.</summary>
public static class MessageConversions
{
    private static readonly JsonFormatter ProtoNames = new(JsonFormatter.Settings.Default.WithPreserveProtoFieldNames(true));

    /// <summary>Returns a new Struct with proto field names; scalar/array JSON roots cannot become Struct.</summary>
    /// <param name="message">The original message, which is never mutated.</param>
    /// <param name="typeRegistry">Explicit descriptors for any opaque Any payloads.</param>
    public static Struct ToStruct(IMessage message, TypeRegistry? typeRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var formatter = typeRegistry is null ? ProtoNames
            : new JsonFormatter(JsonFormatter.Settings.Default.WithPreserveProtoFieldNames(true).WithTypeRegistry(typeRegistry));
        return JsonParser.Default.Parse<Struct>(formatter.Format(message));
    }

    /// <summary>Parses a Struct into a new typed message, leaving the source unchanged.</summary>
    /// <typeparam name="T">The authoritative generated message type.</typeparam>
    /// <param name="value">The protobuf JSON object to parse.</param>
    /// <param name="typeRegistry">Explicit descriptors for any opaque Any payloads.</param>
    public static T ToMessage<T>(Struct value, TypeRegistry? typeRegistry = null) where T : IMessage, new()
    {
        ArgumentNullException.ThrowIfNull(value);
        return Parser(typeRegistry).Parse<T>(ProtoNames.Format(value));
    }

    /// <summary>Parses a Struct into a new message using an authoritative CLR-backed descriptor.</summary>
    /// <param name="value">The protobuf JSON object to parse.</param>
    /// <param name="descriptor">The destination message descriptor.</param>
    /// <param name="typeRegistry">Explicit descriptors for any opaque Any payloads.</param>
    /// <exception cref="ArgumentException">The descriptor has no CLR parser, such as a map-entry descriptor.</exception>
    public static IMessage ToMessage(Struct value, MessageDescriptor descriptor, TypeRegistry? typeRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Parser is null)
        {
            throw new ArgumentException("The descriptor has no CLR parser.", nameof(descriptor));
        }
        return Parser(typeRegistry).Parse(ProtoNames.Format(value), descriptor);
    }

    private static JsonParser Parser(TypeRegistry? typeRegistry) => typeRegistry is null ? JsonParser.Default
        : new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(typeRegistry));
}
