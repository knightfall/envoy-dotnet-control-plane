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
	sotw "github.com/envoyproxy/go-control-plane/pkg/server/sotw/v3"
	"google.golang.org/genproto/googleapis/rpc/status"
)

type streamStep struct {
	Kind         string   `json:"kind"`
	Type         string   `json:"type"`
	Node         *string  `json:"node"`
	Version      string   `json:"version"`
	Nonce        string   `json:"nonce"`
	Names        []string `json:"names"`
	Nack         bool     `json:"nack"`
	Stale        bool     `json:"stale"`
	Watch        int      `json:"watch"`
	ReceiveError bool     `json:"receiveError"`
}

type streamObservation struct {
	Events    []string `json:"events"`
	Disposals []int    `json:"disposals"`
}

type streamCase struct {
	ID            string            `json:"id"`
	DefaultType   string            `json:"defaultType"`
	CallbackNonce string            `json:"callbackNonce"`
	Steps         []streamStep      `json:"steps"`
	Observed      streamObservation `json:"observed"`
}

type streamRegistration struct {
	request   *discovery.DiscoveryRequest
	responses chan cache.Response
	disposals int
}

type streamTrace struct {
	*mockStream
	requested     chan bool
	created       chan *streamRegistration
	registrations []*streamRegistration
	observation   streamObservation
	callbackNonce string
}

func (s *streamTrace) OnStreamOpen(_ context.Context, _ int64, typeURL string) error {
	s.observation.Events = append(s.observation.Events, "open|"+typeURL)
	return nil
}

func (s *streamTrace) OnStreamClosed(_ int64, node *core.Node) {
	s.observation.Events = append(s.observation.Events, "close|"+node.GetId())
}

func (s *streamTrace) OnStreamRequest(_ int64, request *discovery.DiscoveryRequest) error {
	s.observation.Events = append(s.observation.Events, fmt.Sprintf("request|%s|%s|%s|%s|%s|%t", request.TypeUrl, request.Node.GetId(),
		request.VersionInfo, request.ResponseNonce, strings.Join(request.ResourceNames, ","), request.ErrorDetail != nil))
	s.requested <- true
	return nil
}

func (s *streamTrace) OnStreamResponse(_ context.Context, _ int64, _ *discovery.DiscoveryRequest, response *discovery.DiscoveryResponse) {
	s.observation.Events = append(s.observation.Events, fmt.Sprintf("response|%s|%s|%s", response.TypeUrl, response.VersionInfo, response.Nonce))
	if s.callbackNonce != "" {
		response.Nonce = s.callbackNonce
	}
}

func (s *streamTrace) CreateWatch(request *discovery.DiscoveryRequest, subscription cache.Subscription, responses chan cache.Response) (func(), error) {
	returned := make([]string, 0, len(subscription.ReturnedResources()))
	for name, version := range subscription.ReturnedResources() {
		returned = append(returned, name+"="+version)
	}
	sort.Strings(returned)
	s.observation.Events = append(s.observation.Events, fmt.Sprintf("watch|%s|%s|%s|%s|%t", request.TypeUrl, request.VersionInfo,
		strings.Join(request.ResourceNames, ","), strings.Join(returned, ","), subscription.IsWildcard()))
	registration := &streamRegistration{request: request, responses: responses}
	s.registrations = append(s.registrations, registration)
	s.created <- registration
	return func() { registration.disposals++ }, nil
}

func (s *streamTrace) CreateDeltaWatch(*cache.DeltaRequest, cache.Subscription, chan cache.DeltaResponse) (func(), error) {
	return nil, fmt.Errorf("Delta is outside the SotW trace")
}

func runStreams(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle stream <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []streamCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 6 {
		return fmt.Errorf("expected six declared stream traces, got %d", len(cases))
	}
	seen := map[string]bool{}
	for index := range cases {
		fixture := &cases[index]
		if seen[fixture.ID] {
			return fmt.Errorf("duplicate stream trace %s", fixture.ID)
		}
		seen[fixture.ID] = true
		observed, err := executeStream(*fixture)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.ID, err)
		}
		if !reflect.DeepEqual(observed, fixture.Observed) {
			return fmt.Errorf("%s: stream differs: Go=%+v .NET=%+v", fixture.ID, observed, fixture.Observed)
		}
	}
	output, err := json.MarshalIndent(cases, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go SotW streams: %d traces matched callbacks, watches, nodes, versions, nonces and cleanup.\n", len(cases))
	return nil
}

func executeStream(fixture streamCase) (streamObservation, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	trace := &streamTrace{requested: make(chan bool, 1), created: make(chan *streamRegistration, 1), callbackNonce: fixture.CallbackNonce}
	trace.mockStream = makeMockStream(ctx, func(response *discovery.DiscoveryResponse) {
		trace.observation.Events = append(trace.observation.Events, fmt.Sprintf("send|%s|%s|%s", response.TypeUrl, response.VersionInfo, response.Nonce))
	})
	done := make(chan error, 1)
	go func() { done <- sotw.NewServer(ctx, trace, trace).StreamHandler(trace, fixture.DefaultType) }()
	for _, step := range fixture.Steps {
		switch step.Kind {
		case "request":
			request := &discovery.DiscoveryRequest{TypeUrl: step.Type, VersionInfo: step.Version, ResponseNonce: step.Nonce, ResourceNames: step.Names}
			if step.Node != nil {
				request.Node = &core.Node{Id: *step.Node}
			}
			if step.Nack {
				request.ErrorDetail = &status.Status{Code: 3, Message: "rejected"}
			}
			select {
			case trace.recv <- streamInput{request: request}:
			case <-ctx.Done():
				return streamObservation{}, ctx.Err()
			}
			if _, err := nextStreamEvent(ctx, trace.requested); err != nil {
				return streamObservation{}, err
			}
			if !step.Stale {
				if _, err := nextStreamEvent(ctx, trace.created); err != nil {
					return streamObservation{}, err
				}
			}
		case "respond":
			registration := trace.registrations[step.Watch]
			returned := map[string]string{}
			for _, name := range step.Names {
				returned[name] = step.Version
			}
			registration.responses <- &cache.PassthroughResponse{
				Request:           registration.request,
				DiscoveryResponse: &discovery.DiscoveryResponse{TypeUrl: registration.request.TypeUrl, VersionInfo: step.Version},
				ReturnedResources: returned,
			}
			if _, err := nextStreamEvent(ctx, trace.sent); err != nil {
				return streamObservation{}, err
			}
		case "end":
			if step.ReceiveError {
				select {
				case trace.recv <- streamInput{err: io.ErrUnexpectedEOF}:
				case <-ctx.Done():
					return streamObservation{}, ctx.Err()
				}
			} else {
				close(trace.recv)
			}
			err, timeout := nextStreamEvent(ctx, done)
			if timeout != nil {
				return streamObservation{}, timeout
			}
			if err != nil {
				return streamObservation{}, err
			}
		default:
			return streamObservation{}, fmt.Errorf("unknown stream step %s", step.Kind)
		}
	}
	for _, registration := range trace.registrations {
		trace.observation.Disposals = append(trace.observation.Disposals, registration.disposals)
	}
	return trace.observation, nil
}

func nextStreamEvent[T any](ctx context.Context, events <-chan T) (T, error) {
	select {
	case value := <-events:
		return value, nil
	case <-ctx.Done():
		var empty T
		return empty, ctx.Err()
	}
}
