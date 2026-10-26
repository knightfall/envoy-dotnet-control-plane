// Calls the pinned HTTPGateway and shared Fetch engine; adapts TestGateway.
// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"reflect"
	"strings"
	"testing/iotest"
	"time"

	cluster "github.com/envoyproxy/go-control-plane/envoy/config/cluster/v3"
	core "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	"github.com/envoyproxy/go-control-plane/pkg/cache/types"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"github.com/envoyproxy/go-control-plane/pkg/log"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	server "github.com/envoyproxy/go-control-plane/pkg/server/v3"
	"google.golang.org/protobuf/encoding/protojson"
	"google.golang.org/protobuf/types/known/anypb"
	"google.golang.org/protobuf/types/known/durationpb"
	"google.golang.org/protobuf/types/known/structpb"
)

type gatewayInput struct {
	ID, Path, Body, CallbackError        string
	ReadFailure, BrokenAny, MalformedAny bool
	TtlNanoseconds                       *int64
}
type gatewayObservation struct {
	Status        int
	JSON, Request any
}
type gatewayCase struct {
	Input    gatewayInput
	Observed gatewayObservation
}

func runGateway(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle gateway <candidate-report-json> <output-json>")
	}
	data, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []gatewayCase
	if err := json.Unmarshal(data, &cases); err != nil {
		return err
	}
	if len(cases) != 23 {
		return fmt.Errorf("expected twenty-three gateway cases, got %d", len(cases))
	}
	seen := make(map[string]bool)
	for index, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate gateway case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeGateway(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			goJSON, _ := json.Marshal(actual)
			netJSON, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: gateway differs\nGo: %s\n.NET: %s", fixture.Input.ID, goJSON, netJSON)
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
	fmt.Printf("Go HTTP gateway: %d cases matched cleaned paths, protobuf JSON request/response semantics and 200/304/400/404/500.\n", len(cases))
	return nil
}

func observeGateway(input gatewayInput) (gatewayObservation, error) {
	ctx := context.Background()
	store := cache.NewSnapshotCache(false, cache.IDHash{}, &log.DefaultLogger{})
	metadata, err := structpb.NewStruct(map[string]any{"enabled": true, "number": 1.5})
	if err != nil {
		return gatewayObservation{}, err
	}
	entry := types.ResourceWithTTL{Resource: &cluster.Cluster{
		Name: "a", ConnectTimeout: durationpb.New(3 * time.Second),
		ClusterDiscoveryType: &cluster.Cluster_Type{Type: cluster.Cluster_STRICT_DNS},
		Metadata:             &core.Metadata{FilterMetadata: map[string]*structpb.Struct{"settings": metadata}},
	}}
	if input.TtlNanoseconds != nil {
		ttl := time.Duration(*input.TtlNanoseconds)
		entry.TTL = &ttl
	}
	snapshot, err := cache.NewSnapshotWithTTLs("v1", map[string][]types.ResourceWithTTL{resource.ClusterType: {entry}})
	if err != nil {
		return gatewayObservation{}, err
	}
	if err := store.SetSnapshot(ctx, "node", snapshot); err != nil {
		return gatewayObservation{}, err
	}
	observed := gatewayObservation{}
	var callbackFailure error
	hooks := server.CallbackFuncs{
		FetchRequestFunc: func(_ context.Context, request *discovery.DiscoveryRequest) error {
			data, err := (protojson.MarshalOptions{UseProtoNames: true}).Marshal(request)
			if err == nil {
				err = json.Unmarshal(data, &observed.Request)
			}
			callbackFailure = err
			if err != nil {
				return err
			}
			if input.CallbackError == "skip" {
				return fmt.Errorf("wrapped: %w", &types.SkipFetchError{})
			}
			if input.CallbackError == "reject" {
				return errors.New("fixture callback rejection")
			}
			return nil
		},
		FetchResponseFunc: func(_ *discovery.DiscoveryRequest, response *discovery.DiscoveryResponse) {
			if input.MalformedAny {
				response.Resources = append(response.Resources, &anypb.Any{TypeUrl: resource.ClusterType, Value: []byte{0xff}})
			} else if input.BrokenAny {
				response.Resources = append(response.Resources, &anypb.Any{TypeUrl: "type.googleapis.com/unknown.Type"})
			}
		},
	}
	gateway := server.HTTPGateway{Server: server.NewServer(ctx, store, hooks)}
	var body io.Reader = strings.NewReader(input.Body)
	if input.ReadFailure {
		body = iotest.ErrReader(errors.New("fixture read failure"))
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, input.Path, body)
	if err != nil {
		return observed, err
	}
	data, code, fetchError := gateway.ServeHTTP(request)
	observed.Status = code
	if callbackFailure != nil {
		return observed, callbackFailure
	}
	if code == 200 {
		if fetchError != nil {
			return observed, fetchError
		}
		if err := json.Unmarshal(data, &observed.JSON); err != nil {
			return observed, err
		}
	} else if len(data) != 0 {
		return observed, fmt.Errorf("error/skip returned a response body")
	}
	if len(store.GetStatusKeys()) != 0 {
		return observed, fmt.Errorf("gateway created watch status")
	}
	return observed, nil
}
