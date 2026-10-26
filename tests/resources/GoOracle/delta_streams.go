// Copyright 2018 Envoyproxy Authors. Licensed under Apache-2.0.
// The stream fixture adapts mockDeltaStream in pinned pkg/server/v3/delta_test.go.
// Changes record observations and use cancellation-aware handoffs; see ../UPSTREAM-TESTS.md.
package main

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"reflect"
	"sort"
	"strings"
	"time"

	core "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"github.com/envoyproxy/go-control-plane/pkg/server/config"
	delta "github.com/envoyproxy/go-control-plane/pkg/server/delta/v3"
	"google.golang.org/genproto/googleapis/rpc/status"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	grpcstatus "google.golang.org/grpc/status"
)

type deltaStreamStep struct {
	Kind         string            `json:"kind"`
	Type         string            `json:"type"`
	Node         *string           `json:"node"`
	Nonce        string            `json:"nonce"`
	Add          []string          `json:"add"`
	Remove       []string          `json:"remove"`
	Initial      map[string]string `json:"initial"`
	Nack         bool              `json:"nack"`
	Hold         bool              `json:"hold"`
	Reject       bool              `json:"reject"`
	Watch        int               `json:"watch"`
	Version      string            `json:"version"`
	Names        []string          `json:"names"`
	Sent         int               `json:"sent"`
	ReceiveError bool              `json:"receiveError"`
}

type deltaStreamCase struct {
	ID            string            `json:"id"`
	DefaultType   string            `json:"defaultType"`
	RejectOpen    bool              `json:"rejectOpen"`
	DisableLegacy bool              `json:"disableLegacy"`
	DisabledTypes []string          `json:"disabledTypes"`
	CallbackNonce string            `json:"callbackNonce"`
	Mutate        bool              `json:"mutate"`
	Steps         []deltaStreamStep `json:"steps"`
	Observed      streamObservation `json:"observed"`
}

type deltaStreamInput struct {
	request *discovery.DeltaDiscoveryRequest
	err     error
}

type deltaStreamRegistration struct {
	request   *discovery.DeltaDiscoveryRequest
	responses chan cache.DeltaResponse
	disposals int
}

type deltaStreamTrace struct {
	grpc.ServerStream
	ctx           context.Context
	recv          chan deltaStreamInput
	sent          chan bool
	requested     chan bool
	created       chan bool
	release       chan struct{}
	registrations []*deltaStreamRegistration
	observation   streamObservation
	fixture       deltaStreamCase
	held          *discovery.DeltaDiscoveryRequest
	rejected      *discovery.DeltaDiscoveryRequest
}

func (s *deltaStreamTrace) Context() context.Context { return s.ctx }
func (s *deltaStreamTrace) Recv() (*discovery.DeltaDiscoveryRequest, error) {
	select {
	case item, more := <-s.recv:
		if !more {
			return nil, io.EOF
		}
		return item.request, item.err
	case <-s.ctx.Done():
		return nil, s.ctx.Err()
	}
}
func (s *deltaStreamTrace) Send(response *discovery.DeltaDiscoveryResponse) error {
	s.observation.Events = append(s.observation.Events, fmt.Sprintf("send|%s|%s|%s", response.TypeUrl, response.SystemVersionInfo, response.Nonce))
	select {
	case s.sent <- true:
		return nil
	case <-s.ctx.Done():
		return s.ctx.Err()
	}
}
func (s *deltaStreamTrace) OnDeltaStreamOpen(_ context.Context, _ int64, typeURL string) error {
	s.observation.Events = append(s.observation.Events, "open|"+typeURL)
	if s.fixture.RejectOpen {
		return grpcstatus.Error(codes.InvalidArgument, "callback rejected")
	}
	return nil
}
func (s *deltaStreamTrace) OnDeltaStreamClosed(_ int64, node *core.Node) {
	s.observation.Events = append(s.observation.Events, "close|"+node.GetId())
}
func (s *deltaStreamTrace) OnStreamDeltaRequest(_ int64, request *discovery.DeltaDiscoveryRequest) error {
	s.observation.Events = append(s.observation.Events, fmt.Sprintf("request|%s|%s|%s|%s|%s|%t", request.TypeUrl, request.Node.GetId(),
		request.ResponseNonce, strings.Join(request.ResourceNamesSubscribe, ","), strings.Join(request.ResourceNamesUnsubscribe, ","), request.ErrorDetail != nil))
	s.requested <- true
	if request == s.rejected {
		return grpcstatus.Error(codes.InvalidArgument, "callback rejected")
	}
	if request.TypeUrl == "mutate" && s.fixture.Mutate {
		request.TypeUrl = "changed"
		request.Node = &core.Node{Id: "callback"}
		request.ResourceNamesSubscribe = append(request.ResourceNamesSubscribe, "selected")
	}
	if request == s.held {
		select {
		case <-s.release:
		case <-s.ctx.Done():
			return s.ctx.Err()
		}
	}
	return nil
}
func (s *deltaStreamTrace) OnStreamDeltaResponse(_ int64, _ *discovery.DeltaDiscoveryRequest, response *discovery.DeltaDiscoveryResponse) {
	s.observation.Events = append(s.observation.Events, fmt.Sprintf("response|%s|%s|%s", response.TypeUrl, response.SystemVersionInfo, response.Nonce))
	if s.fixture.CallbackNonce != "" {
		response.Nonce = s.fixture.CallbackNonce
	}
}
func (s *deltaStreamTrace) CreateWatch(*discovery.DiscoveryRequest, cache.Subscription, chan cache.Response) (func(), error) {
	return nil, fmt.Errorf("SotW is outside the Delta trace")
}
func (s *deltaStreamTrace) CreateDeltaWatch(request *discovery.DeltaDiscoveryRequest, subscription cache.Subscription, responses chan cache.DeltaResponse) (func(), error) {
	names, prefixes, returned := []string{}, []string{}, []string{}
	for name := range subscription.SubscribedResources() {
		names = append(names, name)
	}
	for prefix := range subscription.SubscribedPrefixes() {
		prefixes = append(prefixes, prefix)
	}
	for name, version := range subscription.ReturnedResources() {
		returned = append(returned, name+"="+version)
	}
	sort.Strings(names)
	sort.Strings(prefixes)
	sort.Strings(returned)
	s.observation.Events = append(s.observation.Events, fmt.Sprintf("watch|%s|%s|%s|%s|%t", request.TypeUrl,
		strings.Join(names, ","), strings.Join(prefixes, ","), strings.Join(returned, ","), subscription.IsWildcard()))
	registration := &deltaStreamRegistration{request: request, responses: responses}
	s.registrations = append(s.registrations, registration)
	s.created <- true
	return func() { registration.disposals++ }, nil
}

func runDeltaStreams(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle delta-stream <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []deltaStreamCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 10 {
		return fmt.Errorf("expected ten Delta stream traces, got %d", len(cases))
	}
	seen := map[string]bool{}
	for index := range cases {
		fixture := &cases[index]
		if seen[fixture.ID] {
			return fmt.Errorf("duplicate Delta stream trace %s", fixture.ID)
		}
		seen[fixture.ID] = true
		actual, err := executeDeltaStream(*fixture)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			return fmt.Errorf("%s: Delta trace mismatch\nGo: %+v\nC#: %+v", fixture.ID, actual, fixture.Observed)
		}
		fixture.Observed = actual
	}
	output, err := json.MarshalIndent(cases, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go Delta streams: %d actual callback, subscription, send and cleanup traces matched.\n", len(cases))
	return nil
}

func executeDeltaStream(fixture deltaStreamCase) (streamObservation, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	trace := &deltaStreamTrace{ctx: ctx, fixture: fixture, recv: make(chan deltaStreamInput), sent: make(chan bool, 2),
		requested: make(chan bool, 1), created: make(chan bool, 1), release: make(chan struct{})}
	trace.observation.Disposals = []int{}
	done := make(chan error, 1)
	var options []config.XDSOption
	if fixture.DisableLegacy {
		options = append(options, delta.DeactivateLegacyWildcard())
	}
	if len(fixture.DisabledTypes) > 0 {
		options = append(options, delta.DeactivateLegacyWildcardForTypes(fixture.DisabledTypes))
	}
	go func() {
		done <- delta.NewServer(ctx, trace, trace, options...).DeltaStreamHandler(trace, fixture.DefaultType)
	}()
	for _, step := range fixture.Steps {
		switch step.Kind {
		case "request", "request-error":
			request := &discovery.DeltaDiscoveryRequest{TypeUrl: step.Type, ResponseNonce: step.Nonce,
				ResourceNamesSubscribe: step.Add, ResourceNamesUnsubscribe: step.Remove, InitialResourceVersions: step.Initial}
			if step.Node != nil {
				request.Node = &core.Node{Id: *step.Node}
			}
			if step.Nack {
				request.ErrorDetail = &status.Status{Code: 3, Message: "rejected"}
			}
			if step.Hold {
				trace.held = request
			}
			if step.Reject {
				trace.rejected = request
			}
			select {
			case trace.recv <- deltaStreamInput{request: request}:
			case <-ctx.Done():
				return streamObservation{}, ctx.Err()
			}
			if step.Kind == "request-error" {
				if err := observeStreamError(ctx, done, &trace.observation); err != nil {
					return streamObservation{}, err
				}
				continue
			}
			if _, err := nextStreamEvent(ctx, trace.requested); err != nil {
				return streamObservation{}, err
			}
			if !step.Hold {
				if _, err := nextStreamEvent(ctx, trace.created); err != nil {
					return streamObservation{}, err
				}
			}
		case "respond", "queue":
			registration := trace.registrations[step.Watch]
			returned := map[string]string{}
			for _, name := range step.Names {
				returned[name] = step.Version
			}
			registration.responses <- &cache.DeltaPassthroughResponse{DeltaRequest: registration.request,
				DeltaDiscoveryResponse: &discovery.DeltaDiscoveryResponse{TypeUrl: registration.request.TypeUrl, SystemVersionInfo: step.Version},
				NextVersionMap:         returned}
			if step.Kind == "respond" {
				if _, err := nextStreamEvent(ctx, trace.sent); err != nil {
					return streamObservation{}, err
				}
			}
		case "release":
			close(trace.release)
			if _, err := nextStreamEvent(ctx, trace.created); err != nil {
				return streamObservation{}, err
			}
			for count := 0; count < step.Sent; count++ {
				if _, err := nextStreamEvent(ctx, trace.sent); err != nil {
					return streamObservation{}, err
				}
			}
		case "error":
			if err := observeStreamError(ctx, done, &trace.observation); err != nil {
				return streamObservation{}, err
			}
		case "end":
			if step.ReceiveError {
				select {
				case trace.recv <- deltaStreamInput{err: io.ErrUnexpectedEOF}:
				case <-ctx.Done():
					return streamObservation{}, ctx.Err()
				}
			} else {
				close(trace.recv)
			}
			failure, err := nextStreamEvent(ctx, done)
			if err != nil {
				return streamObservation{}, err
			}
			if failure != nil {
				return streamObservation{}, failure
			}
		default:
			return streamObservation{}, fmt.Errorf("unknown Delta step %s", step.Kind)
		}
	}
	for _, registration := range trace.registrations {
		trace.observation.Disposals = append(trace.observation.Disposals, registration.disposals)
	}
	return trace.observation, nil
}
