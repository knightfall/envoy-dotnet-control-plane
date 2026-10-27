// Calls pinned rest.Server.Fetch with custom producer responses; no engine logic is copied.
// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"reflect"
	"strings"

	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	rest "github.com/envoyproxy/go-control-plane/pkg/server/rest/v3"
)

type fetchEngineInput struct{ ID, Mode string }
type fetchEngineObservation struct {
	Error                                     string
	Events                                    []string
	CacheType, CallbackType, TypeURL, Version *string
}
type fetchEngineCase struct {
	Input    fetchEngineInput
	Observed fetchEngineObservation
}

func runFetchEngine(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle fetch-engine <candidate-report-json> <output-json>")
	}
	data, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []fetchEngineCase
	if err := json.Unmarshal(data, &cases); err != nil {
		return err
	}
	if len(cases) != 9 {
		return fmt.Errorf("expected nine Fetch engine cases, got %d", len(cases))
	}
	seen := make(map[string]bool)
	for index, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate Fetch engine case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeFetchEngine(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			goJSON, _ := json.Marshal(actual)
			netJSON, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: Fetch engine differs\nGo: %s\n.NET: %s", fixture.Input.ID, goJSON, netJSON)
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
	fmt.Printf("Go Fetch engine: %d callback, producer, materialization, mutation and failure traces matched.\n", len(cases))
	return nil
}

type fetchEngineContextKey struct{}
type fetchEngineFixture struct {
	input    fetchEngineInput
	request  *discovery.DiscoveryRequest
	observed fetchEngineObservation
}

func observeFetchEngine(input fetchEngineInput) (fetchEngineObservation, error) {
	request := &discovery.DiscoveryRequest{TypeUrl: "input/type"}
	fixture := &fetchEngineFixture{input: input, request: request, observed: fetchEngineObservation{Events: []string{}}}
	ctx := context.WithValue(context.Background(), fetchEngineContextKey{}, "context")
	response, err := callFetchEngine(rest.NewServer(fixture, fixture), ctx, request)
	if err != nil {
		if err.Error() == "missing response" {
			fixture.observed.Error = "missing"
		} else if strings.HasPrefix(err.Error(), "fixture-") {
			fixture.observed.Error = strings.TrimPrefix(err.Error(), "fixture-")
		} else {
			return fixture.observed, err
		}
	}
	if response != nil {
		fixture.observed.TypeURL = &response.TypeUrl
		fixture.observed.Version = &response.VersionInfo
	}
	return fixture.observed, nil
}

// Source response hooks can panic; normalize only this fixture's deliberate panic
// to compare the existing .NET exception-propagation policy, without changing the server.
func callFetchEngine(server rest.Server, ctx context.Context, request *discovery.DiscoveryRequest) (response *discovery.DiscoveryResponse, err error) {
	defer func() {
		if failure := recover(); failure != nil {
			if known, ok := failure.(error); ok && known.Error() == "fixture-response-hook" {
				err = known
			} else {
				panic(failure)
			}
		}
	}()
	return server.Fetch(ctx, request)
}

func (fixture *fetchEngineFixture) OnFetchRequest(ctx context.Context, request *discovery.DiscoveryRequest) error {
	fixture.observed.Events = append(fixture.observed.Events, "request")
	if ctx.Value(fetchEngineContextKey{}) != "context" || request != fixture.request {
		return errors.New("request context/identity differs")
	}
	if fixture.input.Mode == "request" {
		return errors.New("fixture-request")
	}
	if fixture.input.Mode == "mutate-request" {
		request.TypeUrl = "hook/type"
	}
	return nil
}
func (fixture *fetchEngineFixture) Fetch(ctx context.Context, request *cache.Request) (cache.Response, error) {
	fixture.observed.Events = append(fixture.observed.Events, "fetch")
	if ctx.Value(fetchEngineContextKey{}) != "context" || request != fixture.request {
		return nil, errors.New("cache context/identity differs")
	}
	fixture.observed.CacheType = &request.TypeUrl
	if fixture.input.Mode == "cache" {
		return nil, errors.New("fixture-cache")
	}
	if fixture.input.Mode == "missing" {
		return nil, nil
	}
	origin := request
	if fixture.input.Mode == "different-origin" {
		origin = &discovery.DiscoveryRequest{TypeUrl: "other-origin"}
	}
	return &fetchEngineResponse{PassthroughResponse: cache.PassthroughResponse{Request: origin,
		DiscoveryResponse: &discovery.DiscoveryResponse{TypeUrl: request.TypeUrl, VersionInfo: "v1"}}, fixture: fixture}, nil
}
func (fixture *fetchEngineFixture) OnFetchResponse(request *discovery.DiscoveryRequest, response *discovery.DiscoveryResponse) {
	if request != fixture.request {
		panic("response callback lost original caller")
	}
	fixture.observed.CallbackType = &request.TypeUrl
	event := "response"
	if response == nil {
		event = "response:null"
	}
	fixture.observed.Events = append(fixture.observed.Events, event)
	if fixture.input.Mode == "response-hook" {
		panic(errors.New("fixture-response-hook"))
	}
	if fixture.input.Mode == "mutate-response" {
		response.VersionInfo = "from-hook"
	}
}

type fetchEngineResponse struct {
	cache.PassthroughResponse
	fixture *fetchEngineFixture
}

func (response *fetchEngineResponse) GetDiscoveryResponse() (*discovery.DiscoveryResponse, error) {
	response.fixture.observed.Events = append(response.fixture.observed.Events, "materialize")
	if response.fixture.input.Mode == "materialize" {
		return nil, errors.New("fixture-materialize")
	}
	return response.PassthroughResponse.DiscoveryResponse, nil
}
