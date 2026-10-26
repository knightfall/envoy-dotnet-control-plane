// Calls the actual pinned MuxCache and reuses the existing Snapshot/Linear fixtures.
package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"
	"reflect"
	"sort"
	"strings"

	core "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	route "github.com/envoyproxy/go-control-plane/envoy/config/route/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"github.com/envoyproxy/go-control-plane/pkg/log"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	stream "github.com/envoyproxy/go-control-plane/pkg/server/stream/v3"
)

type muxCacheInput struct {
	ID, Mode, SotwKey, DeltaKey string
	Cancel, FetchFailure        bool
}
type muxCacheObservation struct {
	Response                                                 *deltaCacheFrame
	Error                                                    string
	SotwClassifications, DeltaClassifications, CustomCalls   int
	BeforeSnapshot, BeforeLinear, AfterSnapshot, AfterLinear int
}
type muxCacheCase struct {
	Input    muxCacheInput
	Observed muxCacheObservation
}

type muxFetchFixture struct {
	calls   int
	failure bool
}

func (*muxFetchFixture) CreateWatch(*cache.Request, cache.Subscription, chan cache.Response) (func(), error) {
	return nil, fmt.Errorf("unexpected custom SotW watch")
}
func (*muxFetchFixture) CreateDeltaWatch(*cache.DeltaRequest, cache.Subscription, chan cache.DeltaResponse) (func(), error) {
	return nil, fmt.Errorf("unexpected custom Delta watch")
}
func (fixture *muxFetchFixture) Fetch(_ context.Context, request *cache.Request) (cache.Response, error) {
	fixture.calls++
	if fixture.failure {
		return nil, fmt.Errorf("fixture child failure")
	}
	return &cache.PassthroughResponse{Request: request,
		DiscoveryResponse: &discovery.DiscoveryResponse{TypeUrl: resource.RouteType, VersionInfo: "custom/v1"},
		ReturnedResources: map[string]string{}}, nil
}

func runMuxCache(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle mux-cache <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []muxCacheCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 12 {
		return fmt.Errorf("expected twelve MuxCache cases, got %d", len(cases))
	}
	results := make([]muxCacheCase, 0, len(cases))
	seen := make(map[string]bool)
	for _, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate MuxCache case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeMuxCache(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			goJSON, _ := json.Marshal(actual)
			dotnetJSON, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: MuxCache differs\nGo: %s\n.NET: %s", fixture.Input.ID, goJSON, dotnetJSON)
		}
		results = append(results, muxCacheCase{Input: fixture.Input, Observed: actual})
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go MuxCache: %d cases matched classification, mixed children, cancellation, Fetch, errors and responses.\n", len(results))
	return nil
}

func observeMuxCache(input muxCacheInput) (muxCacheObservation, error) {
	ctx := context.Background()
	snapshot := cache.NewSnapshotCache(false, cache.IDHash{}, &log.DefaultLogger{})
	linear := cache.NewLinearCache(resource.RouteType, cache.WithLogger(&log.DefaultLogger{}))
	custom := &muxFetchFixture{failure: input.FetchFailure}
	observation := muxCacheObservation{}
	mux := &cache.MuxCache{
		Caches: map[string]cache.Cache{"snapshot": snapshot, "linear": linear, "custom": custom},
		Classify: func(*cache.Request) string {
			observation.SotwClassifications++
			return input.SotwKey
		},
		ClassifyDelta: func(*cache.DeltaRequest) string {
			observation.DeltaClassifications++
			return input.DeltaKey
		},
	}
	request := &discovery.DiscoveryRequest{TypeUrl: resource.RouteType, VersionInfo: "0", Node: &core.Node{Id: "node"}}
	var err error
	if input.Mode == "fetch" {
		var response cache.Response
		response, err = mux.Fetch(ctx, request)
		if err == nil {
			observation.Response, err = muxSotwFrame(response)
		}
	} else {
		sotwResponses := make(chan cache.Response, 1)
		deltaResponses := make(chan cache.DeltaResponse, 1)
		var cancel func()
		if input.Mode == "delta" {
			cancel, err = mux.CreateDeltaWatch(&discovery.DeltaDiscoveryRequest{TypeUrl: resource.RouteType, Node: request.Node, ResponseNonce: "accepted"},
				stream.NewDeltaSubscription([]string{"a"}, nil, map[string]string{}, false), deltaResponses)
		} else {
			cancel, err = mux.CreateWatch(request, stream.NewSotwSubscription([]string{"a"}, false), sotwResponses)
		}
		if err == nil {
			observation.BeforeSnapshot = muxSnapshotCount(snapshot)
			observation.BeforeLinear = linear.NumCacheWatches()
			if input.Cancel {
				cancel()
			} else {
				defer cancel()
			}
			next, buildErr := makeDeltaSnapshot(&deltaCacheSnapshot{Version: "snapshot/v1", Resources: []deltaCacheEntry{{Name: "a"}}})
			if buildErr != nil {
				return observation, buildErr
			}
			if err := snapshot.SetSnapshot(ctx, "node", next); err != nil {
				return observation, err
			}
			if err := linear.UpdateResource("a", &route.RouteConfiguration{Name: "a"}); err != nil {
				return observation, err
			}
			select {
			case response := <-sotwResponses:
				observation.Response, err = muxSotwFrame(response)
			default:
			}
			select {
			case response := <-deltaResponses:
				envelope, materializeErr := response.GetDeltaDiscoveryResponse()
				if materializeErr != nil {
					return observation, materializeErr
				}
				entries := make([]deltaCacheResource, 0, len(envelope.Resources))
				for _, entry := range envelope.Resources {
					entries = append(entries, deltaCacheResource{Name: entry.Name, Version: entry.Version, Data: base64.StdEncoding.EncodeToString(entry.Resource.Value), HasTtl: entry.Ttl != nil})
				}
				sort.Slice(entries, func(i, j int) bool { return entries[i].Name < entries[j].Name })
				removed := append([]string{}, envelope.RemovedResources...)
				sort.Strings(removed)
				observation.Response = &deltaCacheFrame{Version: envelope.SystemVersionInfo, TypeURL: envelope.TypeUrl, Resources: entries, Removed: removed, Returned: response.GetReturnedResources()}
			default:
			}
		}
	}
	if err != nil {
		switch {
		case strings.HasPrefix(err.Error(), "no cache defined for key "):
			observation.Error = "missing"
		case err.Error() == "fixture child failure":
			observation.Error = "child-fetch"
		case err.Error() == "fetch is not implemented by LinearCache":
			observation.Error = "linear-fetch"
		default:
			return observation, err
		}
	}
	observation.CustomCalls = custom.calls
	observation.AfterSnapshot = muxSnapshotCount(snapshot)
	observation.AfterLinear = linear.NumCacheWatches()
	return observation, nil
}

func muxSnapshotCount(snapshot cache.SnapshotCache) int {
	if status := snapshot.GetStatusInfo("node"); status != nil {
		return status.GetNumWatches() + status.GetNumDeltaWatches()
	}
	return 0
}

func muxSotwFrame(response cache.Response) (*deltaCacheFrame, error) {
	envelope, err := response.GetDiscoveryResponse()
	if err != nil {
		return nil, err
	}
	entries := make([]deltaCacheResource, 0, len(envelope.Resources))
	for _, entry := range envelope.Resources {
		entries = append(entries, deltaCacheResource{Data: base64.StdEncoding.EncodeToString(entry.Value)})
	}
	sort.Slice(entries, func(i, j int) bool { return entries[i].Data < entries[j].Data })
	return &deltaCacheFrame{Version: envelope.VersionInfo, TypeURL: envelope.TypeUrl, Resources: entries, Removed: []string{}, Returned: response.GetReturnedResources()}, nil
}
