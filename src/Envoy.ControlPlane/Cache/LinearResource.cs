// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Envoy.ControlPlane.Resources;
using Google.Protobuf;

namespace Envoy.ControlPlane.Cache;

internal sealed class LinearResource
{
    private readonly string cacheVersion;
    private readonly Lazy<ByteString> serialized;
    private readonly Lazy<string> hash;

    internal LinearResource(string name, IMessage? resource, string cacheVersion)
    {
        Name = name;
        Resource = resource;
        this.cacheVersion = cacheVersion;
        serialized = new Lazy<ByteString>(() => ByteString.CopyFrom(ResourceSerialization.Serialize(resource)));
        hash = new Lazy<string>(() => ResourceSerialization.ComputeHash(serialized.Value.Span));
    }

    internal string Name { get; }
    internal IMessage? Resource { get; }
    internal ByteString Payload => serialized.Value;
    internal string GetVersion(bool delta) => delta ? hash.Value : cacheVersion;
}
