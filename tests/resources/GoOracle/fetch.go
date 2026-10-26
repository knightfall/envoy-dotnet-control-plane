// Calls pinned SnapshotCache.Fetch; adapts TestSnapshotCacheFetch/SingleResourceFetch.
// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"reflect"
	"sort"
	"time"

	cluster "github.com/envoyproxy/go-control-plane/envoy/config/cluster/v3"
	core "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	route "github.com/envoyproxy/go-control-plane/envoy/config/route/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	"github.com/envoyproxy/go-control-plane/pkg/cache/types"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"github.com/envoyproxy/go-control-plane/pkg/log"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	"google.golang.org/protobuf/proto"
)

type fetchInput struct {
	ID, TypeURL, GroupType, Version string
	Names                           []string
	Ads, MissingSnapshot            bool
	TtlNanoseconds                  *int64
}
type fetchObservation struct {
	Error, Version, TypeURL string
	Payloads                []string
}
type fetchCase struct {
	Input    fetchInput
	Observed fetchObservation
}

func runFetch(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle fetch <candidate-report-json> <output-json>")
	}
	data, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []fetchCase
	if err := json.Unmarshal(data, &cases); err != nil {
		return err
	}
	if len(cases) != 14 {
		return fmt.Errorf("expected fourteen Fetch cases, got %d", len(cases))
	}
	seen := make(map[string]bool)
	for index, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate Fetch case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeFetch(fixture.Input)
		if err != nil {
			return err
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			return fmt.Errorf("%s: Fetch differs; Go=%+v .NET=%+v", fixture.Input.ID, actual, fixture.Observed)
		}
		cases[index].Observed = actual
	}
	output, err := json.MarshalIndent(cases, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go Fetch: %d cases matched exact/all/duplicate selection, versions, missing/skip errors and TTL wire payloads.\n", len(cases))
	return nil
}

func observeFetch(input fetchInput) (fetchObservation, error) {
	ctx := context.Background()
	store := cache.NewSnapshotCache(input.Ads, cache.IDHash{}, &log.DefaultLogger{})
	if !input.MissingSnapshot {
		var first, second types.Resource = &route.RouteConfiguration{Name: "a"}, &route.RouteConfiguration{Name: "b"}
		if input.GroupType == resource.ClusterType {
			first, second = &cluster.Cluster{Name: "a"}, &cluster.Cluster{Name: "b"}
		}
		entries := []types.ResourceWithTTL{{Resource: first}, {Resource: second}}
		if input.TtlNanoseconds != nil {
			ttl := time.Duration(*input.TtlNanoseconds)
			entries[0].TTL = &ttl
		}
		snapshot, err := cache.NewSnapshotWithTTLs("v1", map[string][]types.ResourceWithTTL{input.GroupType: entries})
		if err != nil {
			return fetchObservation{}, err
		}
		if err := store.SetSnapshot(ctx, "node", snapshot); err != nil {
			return fetchObservation{}, err
		}
	}
	observed := fetchObservation{Payloads: []string{}}
	raw, err := store.Fetch(ctx, &discovery.DiscoveryRequest{Node: &core.Node{Id: "node"}, TypeUrl: input.TypeURL, VersionInfo: input.Version, ResourceNames: input.Names})
	if err != nil {
		var skip *types.SkipFetchError
		if errors.As(err, &skip) {
			observed.Error = "skip"
		} else if input.MissingSnapshot {
			observed.Error = "missing"
		} else {
			return observed, err
		}
	} else {
		response, err := raw.GetDiscoveryResponse()
		if err != nil {
			return observed, err
		}
		if len(raw.GetReturnedResources()) != 0 {
			return observed, fmt.Errorf("Fetch unexpectedly returned a watch ledger")
		}
		observed.Version, observed.TypeURL = response.VersionInfo, response.TypeUrl
		for _, entry := range response.Resources {
			data, err := proto.Marshal(entry)
			if err != nil {
				return observed, err
			}
			observed.Payloads = append(observed.Payloads, base64.StdEncoding.EncodeToString(data))
		}
		if len(input.Names) == 0 {
			sort.Strings(observed.Payloads)
		}
	}
	if len(store.GetStatusKeys()) != 0 {
		return observed, fmt.Errorf("Fetch created watch status")
	}
	return observed, nil
}
