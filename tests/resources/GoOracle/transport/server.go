package main

import (
	"context"
	"encoding/json"
	"errors"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	endpoint "github.com/envoyproxy/go-control-plane/envoy/service/endpoint/v3"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/status"
	"google.golang.org/protobuf/types/known/anypb"
	"io"
	"net"
	"net/http"
	"os"
	"os/signal"
	"runtime"
	"strconv"
	"sync/atomic"
	"syscall"
)

type statistics struct {
	AllocatedBytes uint64  `json:"allocatedBytes"`
	CPUSeconds     float64 `json:"cpuSeconds"`
	Active         int64   `json:"active"`
	BurstStarted   int64   `json:"burstStarted"`
	Attempted      int64   `json:"attemptedWrites"`
	Completed      int64   `json:"completedWrites"`
	Runtime        string  `json:"runtime"`
	Processors     int     `json:"processors"`
}

type probeServer struct {
	discovery.UnimplementedAggregatedDiscoveryServiceServer
	endpoint.UnimplementedEndpointDiscoveryServiceServer
	responses                                  map[int]*discovery.DiscoveryResponse
	active, burstStarted, attempted, completed atomic.Int64
}

func response(size int) *discovery.DiscoveryResponse {
	return &discovery.DiscoveryResponse{VersionInfo: "transport-probe", Nonce: "1", TypeUrl: "type.googleapis.com/probe.Payload",
		Resources: []*anypb.Any{{TypeUrl: "type.googleapis.com/probe.Payload", Value: make([]byte, size)}}}
}

func selectedResponse(ctx context.Context, responses map[int]*discovery.DiscoveryResponse) *discovery.DiscoveryResponse {
	values := metadata.ValueFromIncomingContext(ctx, "size")
	size := 1024
	if len(values) > 0 {
		size, _ = strconv.Atoi(values[0])
	}
	return responses[size]
}

func (s *probeServer) StreamAggregatedResources(stream discovery.AggregatedDiscoveryService_StreamAggregatedResourcesServer) error {
	s.active.Add(1)
	defer s.active.Add(-1)
	if err := stream.SendHeader(metadata.Pairs("probe-header", "present")); err != nil {
		return err
	}
	stream.SetTrailer(metadata.Pairs("probe-trailer", "present"))
	out := selectedResponse(stream.Context(), s.responses)
	for {
		request, err := stream.Recv()
		if errors.Is(err, io.EOF) {
			return nil
		}
		if err != nil {
			return err
		}
		switch request.VersionInfo {
		case "invalid":
			return status.Error(codes.InvalidArgument, "probe rejection")
		case "hold":
			<-stream.Context().Done()
			return stream.Context().Err()
		case "burst":
			s.burstStarted.Store(1)
			for range 20 {
				s.attempted.Add(1)
				if err := stream.Send(out); err != nil {
					return err
				}
				s.completed.Add(1)
			}
		default:
			if err := stream.Send(out); err != nil {
				return err
			}
		}
	}
}

func (s *probeServer) FetchEndpoints(ctx context.Context, _ *discovery.DiscoveryRequest) (*discovery.DiscoveryResponse, error) {
	return selectedResponse(ctx, s.responses), nil
}

func serve(rpcPort, adminPort, certificateDirectory string) {
	s := &probeServer{responses: map[int]*discovery.DiscoveryResponse{}}
	for _, size := range []int{1024, 65536, 1048576, 5 * 1048576} {
		s.responses[size] = response(size)
	}
	options := []grpc.ServerOption{}
	if certificateDirectory != "" {
		options = append(options, grpc.Creds(credentials.NewTLS(tlsSettings(certificateDirectory, true, true))))
	}
	server := grpc.NewServer(options...)
	discovery.RegisterAggregatedDiscoveryServiceServer(server, s)
	endpoint.RegisterEndpointDiscoveryServiceServer(server, s)
	listener, err := net.Listen("tcp", "127.0.0.1:"+rpcPort)
	check(err)
	admin := &http.Server{Addr: "127.0.0.1:" + adminPort, Handler: http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		var memory runtime.MemStats
		runtime.ReadMemStats(&memory)
		cpu := processCPUSeconds()
		check(json.NewEncoder(w).Encode(statistics{memory.TotalAlloc, cpu, s.active.Load(), s.burstStarted.Load(), s.attempted.Load(), s.completed.Load(), runtime.Version(), runtime.GOMAXPROCS(0)}))
	})}
	go func() {
		if err := admin.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			panic(err)
		}
	}()
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer cancel()
	go func() { <-ctx.Done(); server.Stop(); _ = admin.Close() }()
	check(server.Serve(listener))
}
