package main

import (
	"context"
	"crypto/tls"
	"encoding/json"
	"errors"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	endpoint "github.com/envoyproxy/go-control-plane/envoy/service/endpoint/v3"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/peer"
	"google.golang.org/grpc/status"
	"google.golang.org/protobuf/proto"
	"io"
	"net/http"
	"os"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

func connect(address, certificateDirectory string, withIdentity bool) *grpc.ClientConn {
	transport := insecure.NewCredentials()
	if certificateDirectory != "" {
		transport = credentials.NewTLS(tlsSettings(certificateDirectory, false, withIdentity))
	}
	connection, err := grpc.NewClient(address, grpc.WithTransportCredentials(transport), grpc.WithDefaultCallOptions(grpc.MaxCallRecvMsgSize(8*1048576)))
	check(err)
	return connection
}

func outgoing(ctx context.Context, size int) context.Context {
	return metadata.NewOutgoingContext(ctx, metadata.Pairs("size", strconv.Itoa(size)))
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}

func require(ok bool, message string) {
	if !ok {
		panic(message)
	}
}

func validate(out *discovery.DiscoveryResponse, size int) {
	require(out != nil && len(out.Resources) == 1 && len(out.Resources[0].Value) == size, "unexpected response size")
	require(out.TypeUrl == "type.googleapis.com/probe.Payload" && out.VersionInfo == "transport-probe" && out.Nonce == "1", "unexpected response fields")
}

func readStats(address string) statistics {
	client := &http.Client{Timeout: 3 * time.Second}
	r, err := client.Get(address + "/stats")
	check(err)
	defer r.Body.Close()
	var stats statistics
	check(json.NewDecoder(r.Body).Decode(&stats))
	return stats
}

func awaitStats(address string, predicate func(statistics) bool) statistics {
	deadline := time.Now().Add(5 * time.Second)
	for {
		stats := readStats(address)
		if predicate(stats) {
			return stats
		}
		require(time.Now().Before(deadline), "server state did not reach expected condition")
		time.Sleep(5 * time.Millisecond)
	}
}

func emit(value any) { check(json.NewEncoder(os.Stdout).Encode(value)) }

func verify(address, admin, certificateDirectory string) {
	connection := connect(address, certificateDirectory, true)
	defer connection.Close()
	client := discovery.NewAggregatedDiscoveryServiceClient(connection)
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	stream, err := client.StreamAggregatedResources(outgoing(ctx, 1024))
	check(err)
	for range 2 {
		check(stream.Send(&discovery.DiscoveryRequest{VersionInfo: "request"}))
		out, err := stream.Recv()
		check(err)
		validate(out, 1024)
	}
	check(stream.CloseSend())
	_, err = stream.Recv()
	require(errors.Is(err, io.EOF), "half-close did not end cleanly")
	headers, err := stream.Header()
	check(err)
	require(strings.Join(headers.Get("probe-header"), "") == "present" && strings.Join(stream.Trailer().Get("probe-trailer"), "") == "present", "metadata missing")
	var remote peer.Peer
	unary, err := endpoint.NewEndpointDiscoveryServiceClient(connection).FetchEndpoints(outgoing(ctx, 1024), &discovery.DiscoveryRequest{}, grpc.Peer(&remote))
	check(err)
	validate(unary, 1024)
	observed := map[string]any{"test": "DuplexHalfCloseUnaryAndMetadata", "passed": true}
	if certificateDirectory != "" {
		security := remote.AuthInfo.(credentials.TLSInfo)
		expectedVersion, expectedCipher := uint16(tls.VersionTLS12), uint16(tls.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256)
		if os.Getenv("ENVOY_PROBE_TLS_PROTOCOL") == "tls13" {
			expectedVersion, expectedCipher = tls.VersionTLS13, tls.TLS_AES_128_GCM_SHA256
		}
		require(security.State.Version == expectedVersion && security.State.CipherSuite == expectedCipher, "TLS protocol or cipher differs from comparison settings")
		observed["tlsVersion"] = tls.VersionName(security.State.Version)
		observed["cipherSuite"] = tls.CipherSuiteName(security.State.CipherSuite)
	}
	emit(observed)

	stream, err = client.StreamAggregatedResources(outgoing(ctx, 1024))
	check(err)
	check(stream.Send(&discovery.DiscoveryRequest{VersionInfo: "invalid"}))
	_, err = stream.Recv()
	require(status.Code(err) == codes.InvalidArgument && status.Convert(err).Message() == "probe rejection", "status did not survive transport")
	emit(map[string]any{"test": "ExplicitStatus", "passed": true})

	for _, size := range []int{4 * 1048576, 4*1048576 + 1} {
		request := &discovery.DiscoveryRequest{VersionInfo: strings.Repeat("r", size-5)}
		require(proto.Size(request) == size, "incorrect boundary fixture")
		stream, err = client.StreamAggregatedResources(outgoing(ctx, 1024))
		check(err)
		_ = stream.Send(request)
		out, receiveError := stream.Recv()
		if size == 4*1048576 {
			check(receiveError)
			validate(out, 1024)
			check(stream.CloseSend())
			_, receiveError = stream.Recv()
			require(errors.Is(receiveError, io.EOF), "boundary cleanup")
		} else {
			require(status.Code(receiveError) == codes.ResourceExhausted, "oversize request accepted or wrong status")
		}
	}
	out, err := endpoint.NewEndpointDiscoveryServiceClient(connection).FetchEndpoints(outgoing(ctx, 5*1048576), &discovery.DiscoveryRequest{})
	check(err)
	validate(out, 5*1048576)
	emit(map[string]any{"test": "RequestSizeBoundaryAndLargeResponse", "passed": true})

	stream, err = client.StreamAggregatedResources(outgoing(ctx, 1024))
	check(err)
	largeRequest := &discovery.DiscoveryRequest{VersionInfo: strings.Repeat("s", 512*1024)}
	for range 100 {
		check(stream.Send(largeRequest))
		out, err = stream.Recv()
		check(err)
		validate(out, 1024)
	}
	check(stream.CloseSend())
	_, err = stream.Recv()
	require(errors.Is(err, io.EOF), "long request-body stream did not close")
	emit(map[string]any{"test": "StreamingBodyBeyondHttpLimit", "passed": true, "requestBytes": 100 * proto.Size(largeRequest)})

	err = connection.Invoke(ctx, "/envoy.service.discovery.v3.AggregatedDiscoveryService/Unknown", &discovery.DiscoveryRequest{}, &discovery.DiscoveryResponse{})
	require(status.Code(err) == codes.Unimplemented, "unknown method status")
	emit(map[string]any{"test": "UnknownMethod", "passed": true})

	for _, deadline := range []bool{false, true} {
		var holdCtx context.Context
		var stop context.CancelFunc
		if deadline {
			holdCtx, stop = context.WithTimeout(context.Background(), time.Second)
		} else {
			holdCtx, stop = context.WithCancel(context.Background())
		}
		stream, err = client.StreamAggregatedResources(outgoing(holdCtx, 1024))
		check(err)
		check(stream.Send(&discovery.DiscoveryRequest{VersionInfo: "hold"}))
		_, err = stream.Header()
		check(err)
		if !deadline {
			stop()
		}
		_, err = stream.Recv()
		expected := codes.Canceled
		if deadline {
			expected = codes.DeadlineExceeded
		}
		require(status.Code(err) == expected, "wrong cancellation/deadline status")
		stop()
		awaitStats(admin, func(s statistics) bool { return s.Active == 0 })
	}
	emit(map[string]any{"test": "CancellationAndDeadlineCleanup", "passed": true})

	burstCtx, stopBurst := context.WithCancel(context.Background())
	burst, err := client.StreamAggregatedResources(outgoing(burstCtx, 5*1048576))
	check(err)
	check(burst.Send(&discovery.DiscoveryRequest{VersionInfo: "burst"}))
	_, err = burst.Header()
	check(err)
	awaitStats(admin, func(s statistics) bool { return s.BurstStarted == 1 && s.Attempted > s.Completed })
	other, err := client.StreamAggregatedResources(outgoing(ctx, 1024))
	check(err)
	check(other.Send(&discovery.DiscoveryRequest{}))
	out, err = other.Recv()
	check(err)
	validate(out, 1024)
	snapshot := readStats(admin)
	require(snapshot.Completed < 20, "slow reader unexpectedly consumed full burst")
	check(other.CloseSend())
	_, err = other.Recv()
	require(errors.Is(err, io.EOF), "other stream cleanup")
	stopBurst()
	_, _ = burst.Recv()
	awaitStats(admin, func(s statistics) bool { return s.Active == 0 })
	emit(map[string]any{"test": "SlowReaderOtherStreamProgress", "passed": true, "attemptedWrites": snapshot.Attempted, "completedWrites": snapshot.Completed})

	if certificateDirectory != "" {
		anonymous := connect(address, certificateDirectory, false)
		defer anonymous.Close()
		shortCtx, stop := context.WithTimeout(context.Background(), 2*time.Second)
		defer stop()
		_, err = endpoint.NewEndpointDiscoveryServiceClient(anonymous).FetchEndpoints(outgoing(shortCtx, 1024), &discovery.DiscoveryRequest{})
		require(status.Code(err) == codes.Unavailable, "anonymous client passed mutual TLS or failed with an unexpected status")
		valid, validError := endpoint.NewEndpointDiscoveryServiceClient(connection).FetchEndpoints(outgoing(ctx, 1024), &discovery.DiscoveryRequest{})
		check(validError)
		validate(valid, 1024)
		emit(map[string]any{"test": "MutualTlsAdmission", "passed": true, "anonymousStatus": status.Code(err).String()})
	}
}

type workerResult struct {
	count   int
	samples []int64
	ended   time.Time
}

func measure(address, admin, certificateDirectory string, size, workers int, separate bool) {
	connection := connect(address, certificateDirectory, true)
	defer connection.Close()
	client := discovery.NewAggregatedDiscoveryServiceClient(connection)
	ready := make(chan struct{}, workers)
	begin := make(chan struct{})
	results := make(chan workerResult, workers)
	var wait sync.WaitGroup
	var finish time.Time
	for range workers {
		wait.Add(1)
		go func() {
			defer wait.Done()
			workerClient := client
			if separate {
				workerConnection := connect(address, certificateDirectory, true)
				defer workerConnection.Close()
				workerClient = discovery.NewAggregatedDiscoveryServiceClient(workerConnection)
			}
			ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
			defer cancel()
			stream, err := workerClient.StreamAggregatedResources(outgoing(ctx, size))
			check(err)
			request := &discovery.DiscoveryRequest{VersionInfo: "request"}
			for range 100 {
				check(stream.Send(request))
				out, err := stream.Recv()
				check(err)
				validate(out, size)
			}
			ready <- struct{}{}
			<-begin
			result := workerResult{samples: make([]int64, 0, 10000)}
			for time.Now().Before(finish) {
				started := time.Now()
				check(stream.Send(request))
				out, err := stream.Recv()
				check(err)
				validate(out, size)
				result.count++
				result.ended = time.Now()
				if result.count%10 == 0 {
					result.samples = append(result.samples, result.ended.Sub(started).Nanoseconds())
				}
			}
			results <- result
			check(stream.CloseSend())
			_, err = stream.Recv()
			require(errors.Is(err, io.EOF), "measurement stream did not finish")
		}()
	}
	for range workers {
		<-ready
	}
	before := readStats(admin)
	started := time.Now()
	finish = started.Add(2 * time.Second)
	close(begin)
	wait.Wait()
	ended := started
	count := 0
	samples := []int64{}
	for range workers {
		result := <-results
		count += result.count
		samples = append(samples, result.samples...)
		if result.ended.After(ended) {
			ended = result.ended
		}
	}
	after := readStats(admin)
	require(after.Active == 0, "measurement leaked stream")
	sort.Slice(samples, func(i, j int) bool { return samples[i] < samples[j] })
	percentile := func(p float64) float64 { return float64(samples[int(float64(len(samples)-1)*p)]) / 1000 }
	seconds := ended.Sub(started).Seconds()
	connections := 1
	if separate {
		connections = workers
	}
	emit(map[string]any{"size": size, "streams": workers, "connections": connections, "operations": count, "seconds": seconds, "responsesPerSecond": float64(count) / seconds,
		"payloadMiBPerSecond": float64(count*size) / 1048576 / seconds, "p50us": percentile(.5), "p95us": percentile(.95), "p99us": percentile(.99), "latencySamples": len(samples),
		"serverCpuSeconds": after.CPUSeconds - before.CPUSeconds, "allocatedBytesPerResponse": float64(after.AllocatedBytes-before.AllocatedBytes) / float64(count), "runtime": after.Runtime, "processors": after.Processors})
}
