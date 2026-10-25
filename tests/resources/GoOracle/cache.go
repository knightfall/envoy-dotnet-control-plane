package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"
	"reflect"
	"sort"

	core "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	"github.com/envoyproxy/go-control-plane/pkg/cache/types"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protoregistry"
)

type wireCacheSnapshot struct {
	Version   string
	OmitGroup bool
	Data      []string
}

type wireCacheEvent struct {
	Kind     string
	Snapshot *wireCacheSnapshot
}

type cacheResponseObservation struct {
	Version  string            `json:"version"`
	Names    []string          `json:"names"`
	Returned map[string]string `json:"returned"`
}

type cacheObservation struct {
	State             string                    `json:"state"`
	PendingWatchCount *int                      `json:"pendingWatchCount"`
	SnapshotVersion   *string                   `json:"snapshotVersion"`
	Response          *cacheResponseObservation `json:"response"`
}

type cacheCase struct {
	ID             string `json:"id"`
	TypeURL        string `json:"typeUrl"`
	Ads            bool
	Initial        *wireCacheSnapshot
	RequestVersion string
	Names          []string
	Wildcard       bool
	Known          map[string]string
	Prefixes       []string
	Events         []wireCacheEvent
	Observed       []cacheObservation
}

type cacheCaseResult struct {
	ID       string             `json:"id"`
	Observed []cacheObservation `json:"observed"`
}

type cacheSubscription struct {
	known    map[string]string
	names    map[string]struct{}
	prefixes map[string]struct{}
	wildcard bool
}

func (s cacheSubscription) ReturnedResources() map[string]string     { return s.known }
func (s cacheSubscription) SubscribedResources() map[string]struct{} { return s.names }
func (s cacheSubscription) SubscribedPrefixes() map[string]struct{}  { return s.prefixes }
func (s cacheSubscription) IsWildcard() bool                         { return s.wildcard }

func runCache(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle cache <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []cacheCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 23 {
		return fmt.Errorf("expected 23 cache traces, got %d", len(cases))
	}
	seen := make(map[string]bool)
	results := make([]cacheCaseResult, 0, len(cases))
	for _, fixture := range cases {
		if seen[fixture.ID] {
			return fmt.Errorf("duplicate cache trace %s", fixture.ID)
		}
		seen[fixture.ID] = true
		observed, err := compareCache(fixture)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.ID, err)
		}
		if !reflect.DeepEqual(observed, fixture.Observed) {
			actual, _ := json.Marshal(observed)
			expected, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: cache trace differs\nGo: %s\n.NET: %s", fixture.ID, actual, expected)
		}
		results = append(results, cacheCaseResult{ID: fixture.ID, Observed: observed})
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go SnapshotCache: %d event traces matched responses, returned versions, registration and clear/cancel outcomes.\n", len(results))
	return nil
}

func buildCacheSnapshot(fixture *wireCacheSnapshot, typeURL string) (*cache.Snapshot, error) {
	groups := make(map[string][]types.Resource)
	if !fixture.OmitGroup {
		resources := make([]types.Resource, 0, len(fixture.Data))
		for _, encoded := range fixture.Data {
			messageType, err := protoregistry.GlobalTypes.FindMessageByURL(typeURL)
			if err != nil {
				return nil, err
			}
			message := messageType.New().Interface()
			data, err := base64.StdEncoding.DecodeString(encoded)
			if err != nil {
				return nil, err
			}
			if err := proto.Unmarshal(data, message); err != nil {
				return nil, err
			}
			resources = append(resources, message)
		}
		groups[typeURL] = resources
	}
	return cache.NewSnapshot(fixture.Version, groups)
}

func compareCache(fixture cacheCase) ([]cacheObservation, error) {
	instance := cache.NewSnapshotCache(fixture.Ads, cache.IDHash{}, nil)
	publish := func(snapshot *wireCacheSnapshot) error {
		built, err := buildCacheSnapshot(snapshot, fixture.TypeURL)
		if err != nil {
			return err
		}
		return instance.SetSnapshot(context.Background(), "node", built)
	}
	if fixture.Initial != nil {
		if err := publish(fixture.Initial); err != nil {
			return nil, err
		}
	}
	sub := cacheSubscription{known: fixture.Known, names: make(map[string]struct{}), prefixes: make(map[string]struct{}), wildcard: fixture.Wildcard}
	for _, name := range fixture.Names {
		sub.names[name] = struct{}{}
	}
	for _, prefix := range fixture.Prefixes {
		sub.prefixes[prefix] = struct{}{}
	}
	request := &discovery.DiscoveryRequest{Node: &core.Node{Id: "node"}, TypeUrl: fixture.TypeURL,
		VersionInfo: fixture.RequestVersion, ResourceNames: fixture.Names}
	responses := make(chan cache.Response, 1)
	cancel, err := instance.CreateWatch(request, sub, responses)
	if err != nil {
		return nil, err
	}
	if cancel != nil {
		defer cancel()
	}
	canceled := false
	var retained cache.Response
	observe := func() cacheObservation {
		select {
		case response := <-responses:
			retained = response
		default:
		}
		var delivered *cacheResponseObservation
		if retained != nil {
			names := make([]string, 0)
			for _, entry := range retained.(*cache.RawResponse).GetRawResources() {
				names = append(names, cache.GetResourceName(entry.Resource))
			}
			sort.Strings(names)
			returned := make(map[string]string)
			for name, version := range retained.GetReturnedResources() {
				returned[name] = version
			}
			delivered = &cacheResponseObservation{Version: retained.GetResponseVersion(), Names: names, Returned: returned}
		}
		row := cacheObservation{State: "pending", Response: delivered}
		if delivered != nil {
			row.State = "response"
		} else if canceled {
			row.State = "canceled"
		}
		if info := instance.GetStatusInfo("node"); info != nil {
			count := info.GetNumWatches()
			row.PendingWatchCount = &count
		}
		if snapshot, err := instance.GetSnapshot("node"); err == nil {
			version := snapshot.GetVersion(fixture.TypeURL)
			row.SnapshotVersion = &version
		}
		return row
	}
	observed := []cacheObservation{observe()}
	for _, event := range fixture.Events {
		switch event.Kind {
		case "publish":
			if err := publish(event.Snapshot); err != nil {
				return nil, err
			}
		case "clear":
			instance.ClearSnapshot("node")
		case "cancel":
			if cancel != nil {
				cancel()
			}
			canceled = true
		default:
			return nil, fmt.Errorf("unknown cache event %s", event.Kind)
		}
		observed = append(observed, observe())
	}
	return observed, nil
}
