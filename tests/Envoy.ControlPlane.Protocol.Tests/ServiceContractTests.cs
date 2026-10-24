using System.Reflection;
using Envoy.Service.Discovery.V3;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

[Collection(ProtocolBindingCollection.Name)]
public sealed class ServiceContractTests
{
    [Fact]
    public void DiscoveryContracts_AllRequiredClientAndServerShapes_Compile()
    {
        Assert.IsType<ServerServiceDefinition>(Service.Cluster.V3.ClusterDiscoveryService.BindService(new ClusterService()));
        var contracts = DiscoveryContracts.All;
        foreach (var (service, server, client, name, methods) in contracts)
        {
            var descriptor = (ServiceDescriptor)service.GetProperty("Descriptor")!.GetValue(null)!;
            Assert.Equal(name, descriptor.FullName);
            Assert.Equal(methods.Order(StringComparer.Ordinal), descriptor.Methods.Select(m => m.Name).Order(StringComparer.Ordinal));
            Assert.IsType<ClientBase>(Activator.CreateInstance(client, new ContractInvoker()), exactMatch: false);
            Assert.NotNull(server.GetCustomAttribute<BindServiceMethodAttribute>());
            foreach (var method in descriptor.Methods)
            {
                bool streaming = !method.Name.StartsWith("Fetch", StringComparison.Ordinal);
                Assert.Equal(streaming, method.IsClientStreaming);
                Assert.Equal(streaming, method.IsServerStreaming);
                Assert.Equal(method.Name.StartsWith("Delta", StringComparison.Ordinal) ? "envoy.service.discovery.v3.DeltaDiscoveryRequest" : "envoy.service.discovery.v3.DiscoveryRequest", method.InputType.FullName);
                Assert.Equal(method.Name.StartsWith("Delta", StringComparison.Ordinal) ? "envoy.service.discovery.v3.DeltaDiscoveryResponse" : "envoy.service.discovery.v3.DiscoveryResponse", method.OutputType.FullName);
                Assert.Contains(server.GetMethods(), m => m.Name == method.Name);
                Assert.Contains(client.GetMethods(), m => m.Name == method.Name);
                AssertRpcSignatures(server, client, method);
            }
        }
    }

    private static void AssertRpcSignatures(System.Type server, System.Type client, MethodDescriptor method)
    {
        var request = method.InputType.ClrType;
        var response = method.OutputType.ClrType;
        var serverMethod = server.GetMethods().Single(m => m.Name == method.Name);
        System.Type[] parameters;
        System.Type serverReturn;
        System.Type clientReturn;

        if (method.IsClientStreaming)
        {
            parameters = [typeof(IAsyncStreamReader<>).MakeGenericType(request),
                typeof(IServerStreamWriter<>).MakeGenericType(response), typeof(ServerCallContext)];
            serverReturn = typeof(Task);
            clientReturn = typeof(AsyncDuplexStreamingCall<,>).MakeGenericType(request, response);
        }
        else
        {
            parameters = [request, typeof(ServerCallContext)];
            serverReturn = typeof(Task<>).MakeGenericType(response);
            clientReturn = response;
            var asyncReturn = typeof(AsyncUnaryCall<>).MakeGenericType(response);
            var asyncMethods = client.GetMethods().Where(m => m.Name == method.Name + "Async").ToArray();
            Assert.NotEmpty(asyncMethods);
            Assert.All(asyncMethods, m => Assert.Equal(asyncReturn, m.ReturnType));
        }

        Assert.True(serverMethod.IsVirtual);
        Assert.Equal(parameters, serverMethod.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(serverReturn, serverMethod.ReturnType);
        Assert.All(client.GetMethods().Where(m => m.Name == method.Name), m => Assert.Equal(clientReturn, m.ReturnType));
    }

    private sealed class ContractInvoker : CallInvoker
    {
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private sealed class ClusterService : Service.Cluster.V3.ClusterDiscoveryService.ClusterDiscoveryServiceBase;
}
