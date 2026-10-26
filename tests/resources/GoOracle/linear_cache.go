// Fixture adaptations from pkg/cache/v3/linear_test.go at the pinned Go revision.
// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// The driver calls the actual LinearCache; it contains no selection or cache algorithm.
package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"maps"
	"os"
	"reflect"
	"sort"

	cluster "github.com/envoyproxy/go-control-plane/envoy/config/cluster/v3"
	route "github.com/envoyproxy/go-control-plane/envoy/config/route/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	"github.com/envoyproxy/go-control-plane/pkg/cache/types"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"github.com/envoyproxy/go-control-plane/pkg/log"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	stream "github.com/envoyproxy/go-control-plane/pkg/server/stream/v3"
)

type linearCacheEntry struct {
	Name          string
	Changed, Null bool
}
type linearCacheOperation struct {
	Kind      string
	Resources []linearCacheEntry
	Delete    []string
}
type linearCacheInput struct {
	ID, TypeURL, Prefix, Version, Nonce, ProbeName string
	Delta                                          bool
	Initial                                        []linearCacheEntry
	Before                                         []linearCacheOperation
	Operation                                      *linearCacheOperation
	Subscribed                                     []string
	Known                                          map[string]string
}
type linearCacheObservation struct {
	Response                                                           *deltaCacheFrame
	InitialWatches, InitialMatching, InitialPrefixes, InitialWildcards int
	Watches, Resources                                                 int
	Names                                                              []string
}
type linearCacheCase struct {
	Input    linearCacheInput
	Observed linearCacheObservation
}

func runLinearCache(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle linear-cache <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []linearCacheCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 31 {
		return fmt.Errorf("expected 31 LinearCache cases, got %d", len(cases))
	}
	results := make([]linearCacheCase, 0, len(cases))
	seen := make(map[string]bool)
	for _, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate LinearCache case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeLinearCache(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			goJSON, _ := json.Marshal(actual)
			dotnetJSON, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: LinearCache differs\nGo: %s\n.NET: %s", fixture.Input.ID, goJSON, dotnetJSON)
		}
		results = append(results, linearCacheCase{Input: fixture.Input, Observed: actual})
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go LinearCache: %d cases matched payloads, epochs/hashes, removals and watch indexes.\n", len(results))
	return nil
}

func linearResources(entries []linearCacheEntry, typeURL string) map[string]types.Resource {
	result := make(map[string]types.Resource, len(entries))
	for _, entry := range entries {
		if entry.Null {
			result[entry.Name] = nil
		} else if typeURL == resource.ClusterType {
			value := &cluster.Cluster{Name: entry.Name}
			if entry.Changed {
				value.AltStatName = "changed"
			}
			result[entry.Name] = value
		} else {
			result[entry.Name] = &route.RouteConfiguration{Name: entry.Name, IgnorePortInHostMatching: entry.Changed}
		}
	}
	return result
}

func applyLinearOperation(store *cache.LinearCache, operation linearCacheOperation, typeURL string) error {
	updated := linearResources(operation.Resources, typeURL)
	switch operation.Kind {
	case "update":
		for name, value := range updated {
			return store.UpdateResource(name, value)
		}
	case "delete":
		return store.DeleteResource(operation.Delete[0])
	case "bulk":
		return store.UpdateResources(updated, operation.Delete)
	case "set":
		return store.SetResources(updated)
	}
	return fmt.Errorf("unknown LinearCache operation %s", operation.Kind)
}

func observeLinearCache(input linearCacheInput) (linearCacheObservation, error) {
	store := cache.NewLinearCache(input.TypeURL, cache.WithInitialResources(linearResources(input.Initial, input.TypeURL)),
		cache.WithVersionPrefix(input.Prefix), cache.WithLogger(&log.DefaultLogger{}))
	for _, operation := range input.Before {
		if err := applyLinearOperation(store, operation, input.TypeURL); err != nil {
			return linearCacheObservation{}, err
		}
	}
	subscription := stream.NewSotwSubscription(input.Subscribed, false)
	if input.Delta {
		subscription = stream.NewDeltaSubscription(input.Subscribed, nil, maps.Clone(input.Known), false)
	} else {
		subscription.SetReturnedResources(maps.Clone(input.Known))
	}
	sotwResponses := make(chan cache.Response, 1)
	deltaResponses := make(chan cache.DeltaResponse, 1)
	var cancel func()
	var err error
	if input.Delta {
		cancel, err = store.CreateDeltaWatch(&discovery.DeltaDiscoveryRequest{TypeUrl: input.TypeURL, ResponseNonce: input.Nonce}, subscription, deltaResponses)
	} else {
		cancel, err = store.CreateWatch(&discovery.DiscoveryRequest{TypeUrl: input.TypeURL, VersionInfo: input.Version}, subscription, sotwResponses)
	}
	if err != nil {
		return linearCacheObservation{}, err
	}
	if cancel != nil {
		defer cancel()
	}
	observation := linearCacheObservation{InitialWatches: store.NumCacheWatches(), InitialMatching: store.NumWatches(input.ProbeName),
		InitialPrefixes: store.NumPrefixWatches(), InitialWildcards: store.NumWildcardWatches()}
	if input.Operation != nil {
		if err := applyLinearOperation(store, *input.Operation, input.TypeURL); err != nil {
			return observation, err
		}
	}
	observation.Watches = store.NumCacheWatches()
	observation.Resources = store.NumResources()
	observation.Names = make([]string, 0, observation.Resources)
	for name := range store.GetResources() {
		observation.Names = append(observation.Names, name)
	}
	sort.Strings(observation.Names)
	select {
	case response := <-sotwResponses:
		envelope, err := response.GetDiscoveryResponse()
		if err != nil {
			return observation, err
		}
		entries := make([]deltaCacheResource, 0, len(envelope.Resources))
		for _, entry := range envelope.Resources {
			entries = append(entries, deltaCacheResource{Data: base64.StdEncoding.EncodeToString(entry.Value)})
		}
		sort.Slice(entries, func(i, j int) bool { return entries[i].Data < entries[j].Data })
		observation.Response = &deltaCacheFrame{Version: envelope.VersionInfo, TypeURL: envelope.TypeUrl, Resources: entries,
			Removed: []string{}, Returned: response.GetReturnedResources()}
	default:
	}
	select {
	case response := <-deltaResponses:
		envelope, err := response.GetDeltaDiscoveryResponse()
		if err != nil {
			return observation, err
		}
		entries := make([]deltaCacheResource, 0, len(envelope.Resources))
		for _, entry := range envelope.Resources {
			entries = append(entries, deltaCacheResource{Name: entry.Name, Version: entry.Version, Data: base64.StdEncoding.EncodeToString(entry.Resource.Value), HasTtl: entry.Ttl != nil})
		}
		sort.Slice(entries, func(i, j int) bool { return entries[i].Name < entries[j].Name })
		removed := append([]string{}, envelope.RemovedResources...)
		sort.Strings(removed)
		observation.Response = &deltaCacheFrame{Version: envelope.SystemVersionInfo, TypeURL: envelope.TypeUrl, Resources: entries,
			Removed: removed, Returned: response.GetReturnedResources()}
	default:
	}
	return observation, nil
}
