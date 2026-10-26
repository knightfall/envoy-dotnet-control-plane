// Calls the pinned heartbeat cache, adapting TestSnapshotCacheWithTTL's observations.
// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
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
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"github.com/envoyproxy/go-control-plane/pkg/log"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
)

type heartbeatInput struct {
	ID                                     string
	Normal, Wildcard, Ads, MissingSnapshot bool
	Names, Prefixes                        []string
	Known                                  map[string]string
	Resources                              []deltaCacheEntry
}
type heartbeatResource struct {
	Name           string
	Wrapped        bool
	TtlNanoseconds *int64
	HasPayload     bool
	Data, Version  string
}
type heartbeatFrame struct {
	Version, TypeURL string
	Resources        []heartbeatResource
	Returned         map[string]string
}
type heartbeatObservation struct {
	Response           *heartbeatFrame
	Watches, Resources int
}
type heartbeatCase struct {
	Input    heartbeatInput
	Observed heartbeatObservation
}

func runHeartbeats(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle heartbeats <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []heartbeatCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 13 {
		return fmt.Errorf("expected thirteen heartbeat cases, got %d", len(cases))
	}
	results := make([]heartbeatCase, 0, len(cases))
	seen := make(map[string]bool)
	for _, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate heartbeat case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeHeartbeats(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			goJSON, _ := json.Marshal(actual)
			dotnetJSON, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: heartbeat differs\nGo: %s\n.NET: %s", fixture.Input.ID, goJSON, dotnetJSON)
		}
		results = append(results, heartbeatCase{Input: fixture.Input, Observed: actual})
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go heartbeats: %d cases matched TTL payloads, versions/metadata, selection and one-shot counts after an observed scan.\n", len(results))
	return nil
}

func observeHeartbeats(input heartbeatInput) (heartbeatObservation, error) {
	ctx, stop := context.WithTimeout(context.Background(), 10*time.Second)
	defer stop()
	var store cache.SnapshotCache
	if input.Normal {
		store = cache.NewSnapshotCache(input.Ads, cache.IDHash{}, &log.DefaultLogger{})
	} else {
		store = cache.NewSnapshotCacheWithHeartbeating(ctx, input.Ads, cache.IDHash{}, &log.DefaultLogger{}, 10*time.Millisecond)
	}
	if !input.MissingSnapshot {
		snapshot, err := makeDeltaSnapshot(&deltaCacheSnapshot{Version: "v1", Resources: input.Resources})
		if err != nil {
			return heartbeatObservation{}, err
		}
		if err := store.SetSnapshot(ctx, "node", snapshot); err != nil {
			return heartbeatObservation{}, err
		}
	}
	names := make(map[string]struct{})
	prefixes := make(map[string]struct{})
	for _, name := range input.Names {
		names[name] = struct{}{}
	}
	for _, prefix := range input.Prefixes {
		prefixes[prefix] = struct{}{}
	}
	subscription := cacheSubscription{known: maps.Clone(input.Known), names: names, prefixes: prefixes, wildcard: input.Wildcard}
	version := "v1"
	if input.Normal {
		version = ""
	}
	responses := make(chan cache.Response, 1)
	registration, err := store.CreateWatch(&discovery.DiscoveryRequest{Node: &core.Node{Id: "node"}, TypeUrl: resource.RouteType, VersionInfo: version}, subscription, responses)
	if err != nil {
		return heartbeatObservation{}, err
	}
	if registration != nil {
		defer registration()
	}
	if !input.Normal {
		ttl := int64(5 * time.Second)
		sentinel, err := makeDeltaSnapshot(&deltaCacheSnapshot{Version: "v1", Resources: []deltaCacheEntry{{Name: "sentinel", TtlNanoseconds: &ttl}}})
		if err != nil {
			return heartbeatObservation{}, err
		}
		if err := store.SetSnapshot(ctx, "sentinel", sentinel); err != nil {
			return heartbeatObservation{}, err
		}
		observedTick := make(chan cache.Response, 1)
		cancelSentinel, err := store.CreateWatch(&discovery.DiscoveryRequest{Node: &core.Node{Id: "sentinel"}, TypeUrl: resource.RouteType, VersionInfo: "v1"},
			cacheSubscription{known: map[string]string{"sentinel": "v1"}, names: map[string]struct{}{"sentinel": {}}, prefixes: map[string]struct{}{}}, observedTick)
		if err != nil {
			return heartbeatObservation{}, err
		}
		if cancelSentinel != nil {
			defer cancelSentinel()
		}
		select {
		case <-observedTick:
		case <-ctx.Done():
			return heartbeatObservation{}, fmt.Errorf("no heartbeat scan observed: %w", ctx.Err())
		}
		// This acquires the source cache's read lock after the complete heartbeat scan.
		_ = store.GetStatusInfo("node")
	}
	observation := heartbeatObservation{Watches: store.GetStatusInfo("node").GetNumWatches()}
	if snapshot, err := store.GetSnapshot("node"); err == nil {
		observation.Resources = len(snapshot.GetResources(resource.RouteType))
	}
	select {
	case response := <-responses:
		envelope, err := response.GetDiscoveryResponse()
		if err != nil {
			return observation, err
		}
		entries := make([]heartbeatResource, 0, len(envelope.Resources))
		for _, entry := range envelope.Resources {
			if entry.MessageIs(&discovery.Resource{}) {
				var wrapper discovery.Resource
				if err := entry.UnmarshalTo(&wrapper); err != nil {
					return observation, err
				}
				nanoseconds := (wrapper.Ttl.Seconds * 1_000_000_000) + int64(wrapper.Ttl.Nanos)
				value := heartbeatResource{Name: wrapper.Name, Wrapped: true, TtlNanoseconds: &nanoseconds, HasPayload: wrapper.Resource != nil, Version: wrapper.Version}
				if wrapper.Resource != nil {
					value.Data = base64.StdEncoding.EncodeToString(wrapper.Resource.Value)
				}
				entries = append(entries, value)
			} else {
				var message route.RouteConfiguration
				if err := entry.UnmarshalTo(&message); err != nil {
					return observation, err
				}
				entries = append(entries, heartbeatResource{Name: message.Name, HasPayload: true, Data: base64.StdEncoding.EncodeToString(entry.Value)})
			}
		}
		sort.Slice(entries, func(i, j int) bool { return entries[i].Name < entries[j].Name })
		observation.Response = &heartbeatFrame{Version: envelope.VersionInfo, TypeURL: envelope.TypeUrl, Resources: entries, Returned: response.GetReturnedResources()}
	default:
	}
	return observation, nil
}
