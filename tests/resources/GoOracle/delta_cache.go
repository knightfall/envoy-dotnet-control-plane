package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"maps"
	"os"
	"reflect"
	"sort"
	"time"

	core "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	route "github.com/envoyproxy/go-control-plane/envoy/config/route/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	"github.com/envoyproxy/go-control-plane/pkg/cache/types"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"github.com/envoyproxy/go-control-plane/pkg/log"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	stream "github.com/envoyproxy/go-control-plane/pkg/server/stream/v3"
)

type deltaCacheEntry struct {
	Name           string
	Changed        bool
	TtlNanoseconds *int64
}
type deltaCacheSnapshot struct {
	Version   string
	Resources []deltaCacheEntry
}
type deltaCacheInput struct {
	ID                       string
	Before, After            *deltaCacheSnapshot
	Subscribed, Unsubscribed []string
	Known                    map[string]string
	Legacy                   bool
	Nonce                    string
}
type deltaCacheResource struct {
	Name, Version, Data string
	HasTtl              bool
}
type deltaCacheFrame struct {
	Version, TypeURL string
	Resources        []deltaCacheResource
	Removed          []string
	Returned         map[string]string
}
type deltaCacheObservation struct {
	Response        *deltaCacheFrame
	Watches         int
	VersionMapBuilt bool
}
type deltaCacheCase struct {
	Input    deltaCacheInput
	Observed deltaCacheObservation
}

func makeDeltaSnapshot(input *deltaCacheSnapshot) (*cache.Snapshot, error) {
	entries := make([]types.ResourceWithTTL, 0, len(input.Resources))
	for _, entry := range input.Resources {
		value := types.ResourceWithTTL{Resource: &route.RouteConfiguration{Name: entry.Name, IgnorePortInHostMatching: entry.Changed}}
		if entry.TtlNanoseconds != nil {
			ttl := time.Duration(*entry.TtlNanoseconds)
			value.TTL = &ttl
		}
		entries = append(entries, value)
	}
	return cache.NewSnapshotWithTTLs(input.Version, map[resource.Type][]types.ResourceWithTTL{resource.RouteType: entries})
}

func runDeltaCache(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle delta-cache <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []deltaCacheCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 14 {
		return fmt.Errorf("expected fourteen Delta cache cases, got %d", len(cases))
	}
	results := make([]deltaCacheCase, 0, len(cases))
	seen := make(map[string]bool)
	for _, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate Delta cache case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeDeltaCache(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			goJSON, _ := json.Marshal(actual)
			dotnetJSON, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: Delta cache differs\nGo: %s\n.NET: %s", fixture.Input.ID, goJSON, dotnetJSON)
		}
		results = append(results, deltaCacheCase{Input: fixture.Input, Observed: actual})
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go Delta cache: %d cases matched payload/version/removal metadata, status and lazy hashes.\n", len(results))
	return nil
}

func observeDeltaCache(input deltaCacheInput) (deltaCacheObservation, error) {
	ctx := context.Background()
	store := cache.NewSnapshotCache(true, cache.IDHash{}, &log.DefaultLogger{})
	var snapshot *cache.Snapshot
	if input.Before != nil {
		var err error
		snapshot, err = makeDeltaSnapshot(input.Before)
		if err != nil {
			return deltaCacheObservation{}, err
		}
		if err := store.SetSnapshot(ctx, "node", snapshot); err != nil {
			return deltaCacheObservation{}, err
		}
	}
	request := &discovery.DeltaDiscoveryRequest{Node: &core.Node{Id: "node"}, TypeUrl: resource.RouteType, ResponseNonce: input.Nonce}
	// The source owns/mutates this map; preserve the recorded fixture input.
	subscription := stream.NewDeltaSubscription(input.Subscribed, input.Unsubscribed, maps.Clone(input.Known), input.Legacy)
	responses := make(chan cache.DeltaResponse, 1)
	cancel, err := store.CreateDeltaWatch(request, subscription, responses)
	if err != nil {
		return deltaCacheObservation{}, err
	}
	defer cancel()
	if input.After != nil {
		snapshot, err = makeDeltaSnapshot(input.After)
		if err != nil {
			return deltaCacheObservation{}, err
		}
		if err := store.SetSnapshot(ctx, "node", snapshot); err != nil {
			return deltaCacheObservation{}, err
		}
	}
	observation := deltaCacheObservation{Watches: store.GetStatusInfo("node").GetNumDeltaWatches(), VersionMapBuilt: snapshot != nil && snapshot.GetVersionMap(resource.RouteType) != nil}
	select {
	case response := <-responses:
		envelope, err := response.GetDeltaDiscoveryResponse()
		if err != nil {
			return observation, err
		}
		resources := make([]deltaCacheResource, 0, len(envelope.Resources))
		for _, entry := range envelope.Resources {
			resources = append(resources, deltaCacheResource{Name: entry.Name, Version: entry.Version, Data: base64.StdEncoding.EncodeToString(entry.Resource.Value), HasTtl: entry.Ttl != nil})
		}
		sort.Slice(resources, func(i, j int) bool { return resources[i].Name < resources[j].Name })
		removed := append([]string{}, envelope.RemovedResources...)
		sort.Strings(removed)
		observation.Response = &deltaCacheFrame{Version: envelope.SystemVersionInfo, TypeURL: envelope.TypeUrl, Resources: resources, Removed: removed, Returned: response.GetReturnedResources()}
	default:
	}
	return observation, nil
}
