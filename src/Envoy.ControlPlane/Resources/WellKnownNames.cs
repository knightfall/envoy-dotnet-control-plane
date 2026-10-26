// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
namespace Envoy.ControlPlane.Resources;

/// <summary>Legacy conventional extension labels; protobuf typed-config URLs determine extension identity.</summary>
/// <remarks>The upstream wellknown catalog is deprecated and receives no new labels.</remarks>
public static class WellKnownNames
{
    /// <summary>The conventional Buffer label.</summary>
    public const string Buffer = "envoy.filters.http.buffer";

    /// <summary>The conventional CORS label.</summary>
    public const string Cors = "envoy.filters.http.cors";

    /// <summary>The conventional Dynamo label.</summary>
    public const string Dynamo = "envoy.filters.http.dynamo";

    /// <summary>The conventional Fault label.</summary>
    public const string Fault = "envoy.filters.http.fault";

    /// <summary>The conventional GRPCHTTP1Bridge label.</summary>
    public const string GrpcHttp1Bridge = "envoy.filters.http.grpc_http1_bridge";

    /// <summary>The conventional GRPCJSONTranscoder label.</summary>
    public const string GrpcJsonTranscoder = "envoy.filters.http.grpc_json_transcoder";

    /// <summary>The conventional GRPCWeb label.</summary>
    public const string GrpcWeb = "envoy.filters.http.grpc_web";

    /// <summary>The conventional Gzip label.</summary>
    public const string Gzip = "envoy.filters.http.gzip";

    /// <summary>The conventional IPTagging label.</summary>
    public const string IpTagging = "envoy.filters.http.ip_tagging";

    /// <summary>The conventional HTTPRateLimit label.</summary>
    public const string HttpRateLimit = "envoy.filters.http.ratelimit";

    /// <summary>The conventional Router label.</summary>
    public const string Router = "envoy.filters.http.router";

    /// <summary>The conventional HealthCheck label.</summary>
    public const string HealthCheck = "envoy.filters.http.health_check";

    /// <summary>The conventional Lua label.</summary>
    public const string Lua = "envoy.filters.http.lua";

    /// <summary>The conventional Squash label.</summary>
    public const string Squash = "envoy.filters.http.squash";

    /// <summary>The conventional HTTPExternalAuthorization label.</summary>
    public const string HttpExternalAuthorization = "envoy.filters.http.ext_authz";

    /// <summary>The conventional HTTPRoleBasedAccessControl label.</summary>
    public const string HttpRoleBasedAccessControl = "envoy.filters.http.rbac";

    /// <summary>The conventional HTTPGRPCStats label.</summary>
    public const string HttpGrpcStats = "envoy.filters.http.grpc_stats";

    /// <summary>The conventional HTTPWasm label.</summary>
    public const string HttpWasm = "envoy.extensions.filters.http.wasm.v3.Wasm";

    /// <summary>The conventional ClientSSLAuth label.</summary>
    public const string ClientSslAuth = "envoy.filters.network.client_ssl_auth";

    /// <summary>The conventional Echo label.</summary>
    public const string Echo = "envoy.filters.network.echo";

    /// <summary>The conventional HTTPConnectionManager label.</summary>
    public const string HttpConnectionManager = "envoy.filters.network.http_connection_manager";

    /// <summary>The conventional TCPProxy label.</summary>
    public const string TcpProxy = "envoy.filters.network.tcp_proxy";

    /// <summary>The conventional RateLimit label.</summary>
    public const string RateLimit = "envoy.filters.network.ratelimit";

    /// <summary>The conventional MongoProxy label.</summary>
    public const string MongoProxy = "envoy.filters.network.mongo_proxy";

    /// <summary>The conventional ThriftProxy label.</summary>
    public const string ThriftProxy = "envoy.filters.network.thrift_proxy";

    /// <summary>The conventional RedisProxy label.</summary>
    public const string RedisProxy = "envoy.filters.network.redis_proxy";

    /// <summary>The conventional MySQLProxy label.</summary>
    public const string MySqlProxy = "envoy.filters.network.mysql_proxy";

    /// <summary>The conventional ExternalAuthorization label.</summary>
    public const string ExternalAuthorization = "envoy.filters.network.ext_authz";

    /// <summary>The conventional RoleBasedAccessControl label.</summary>
    public const string RoleBasedAccessControl = "envoy.filters.network.rbac";

    /// <summary>The conventional OriginalDestination label.</summary>
    public const string OriginalDestination = "envoy.filters.listener.original_dst";

    /// <summary>The conventional ProxyProtocol label.</summary>
    public const string ProxyProtocol = "envoy.filters.listener.proxy_protocol";

    /// <summary>The conventional TlsInspector label.</summary>
    public const string TlsInspector = "envoy.filters.listener.tls_inspector";

    /// <summary>The conventional HttpInspector label.</summary>
    public const string HttpInspector = "envoy.filters.listener.http_inspector";

    /// <summary>The conventional OriginalSource label.</summary>
    public const string OriginalSource = "envoy.filters.listener.original_src";

    /// <summary>The conventional Lightstep label.</summary>
    public const string Lightstep = "envoy.tracers.lightstep";

    /// <summary>The conventional Zipkin label.</summary>
    public const string Zipkin = "envoy.tracers.zipkin";

    /// <summary>The conventional DynamicOT label.</summary>
    public const string DynamicOt = "envoy.tracers.dynamic_ot";

    /// <summary>The conventional Datadog label.</summary>
    public const string Datadog = "envoy.tracers.datadog";

    /// <summary>The conventional Statsd label.</summary>
    public const string Statsd = "envoy.stat_sinks.statsd";

    /// <summary>The conventional DogStatsd label.</summary>
    public const string DogStatsd = "envoy.stat_sinks.dog_statsd";

    /// <summary>The conventional MetricsService label.</summary>
    public const string MetricsService = "envoy.stat_sinks.metrics_service";

    /// <summary>The conventional FileAccessLog label.</summary>
    public const string FileAccessLog = "envoy.access_loggers.file";

    /// <summary>The conventional HTTPGRPCAccessLog label.</summary>
    public const string HttpGrpcAccessLog = "envoy.access_loggers.http_grpc";

    /// <summary>The conventional TransportSocketAlts label.</summary>
    public const string TransportSocketAlts = "envoy.transport_sockets.alts";

    /// <summary>The conventional TransportSocketTap label.</summary>
    public const string TransportSocketTap = "envoy.transport_sockets.tap";

    /// <summary>The conventional TransportSocketRawBuffer label.</summary>
    public const string TransportSocketRawBuffer = "envoy.transport_sockets.raw_buffer";

    /// <summary>The conventional TransportSocketTls label.</summary>
    public const string TransportSocketTls = "envoy.transport_sockets.tls";

    /// <summary>The conventional TransportSocketQuic label.</summary>
    public const string TransportSocketQuic = "envoy.transport_sockets.quic";
}
