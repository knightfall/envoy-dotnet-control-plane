// Calls the pinned ADS client through gRPC's connection interface; no client state logic is copied.
// Scenario adaptation from client/sotw/v3/client_test.go, Copyright 2022 Envoyproxy Authors.
// SPDX-License-Identifier: Apache-2.0
package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"reflect"

	core "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	route "github.com/envoyproxy/go-control-plane/envoy/config/route/v3"
	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	client "github.com/envoyproxy/go-control-plane/pkg/client/sotw/v3"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/metadata"
	"google.golang.org/grpc/status"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/types/known/anypb"
)

var errAdsSend = errors.New("fixture-send")
var errAdsOpen = errors.New("fixture-open")

type adsClientStep struct {
	Operation, Version, Nonce string
	Names                     []string
}
type adsClientInput struct {
	ID    string
	Steps []adsClientStep
}
type adsClientRequest struct {
	Version, Nonce string
	Error          *string
	Call, Code     int
}
type adsClientError struct {
	Kind       string
	Connection bool
}
type adsClientObservation struct {
	Requests  []adsClientRequest
	Errors    []adsClientError
	Resources [][]string
}
type adsClientCase struct {
	Input    adsClientInput
	Observed adsClientObservation
}

func runAdsClient(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle ads-client <candidate-report-json> <output-json>")
	}
	data, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []adsClientCase
	if err := json.Unmarshal(data, &cases); err != nil {
		return err
	}
	if len(cases) != 12 {
		return fmt.Errorf("expected twelve ADS client cases, got %d", len(cases))
	}
	seen := make(map[string]bool)
	for index, fixture := range cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate ADS client case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeAdsClient(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if !reflect.DeepEqual(actual, fixture.Observed) {
			goJSON, _ := json.Marshal(actual)
			netJSON, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: ADS client differs\nGo: %s\n.NET: %s", fixture.Input.ID, goJSON, netJSON)
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
	fmt.Printf("Go ADS client: %d cases matched initial requests, resource lists, ACK/NACK state, failed sends/receives, reinit and EOF/error classification.\n", len(cases))
	return nil
}

func observeAdsClient(input adsClientInput) (adsClientObservation, error) {
	ctx := context.Background()
	connection := &scriptedAdsConnection{}
	ads := client.NewADSClient(ctx, &core.Node{Id: "node"}, resource.RouteType)
	observed := adsClientObservation{Requests: []adsClientRequest{}, Errors: []adsClientError{}, Resources: [][]string{}}
	for _, step := range input.Steps {
		var err error
		switch step.Operation {
		case "init":
			err = ads.InitConnect(connection)
		case "open-error-init":
			connection.openFailure = errAdsOpen
			err = ads.InitConnect(connection)
		case "send-error-init":
			connection.nextSendFailure = errAdsSend
			err = ads.InitConnect(connection)
		case "receive":
			response := &discovery.DiscoveryResponse{VersionInfo: step.Version, Nonce: step.Nonce, TypeUrl: resource.RouteType}
			for _, name := range step.Names {
				entry, packError := anypb.New(&route.RouteConfiguration{Name: name})
				if packError != nil {
					return observed, packError
				}
				response.Resources = append(response.Resources, entry)
			}
			connection.latest().response = response
			var result *client.Response
			result, err = ads.Fetch()
			if err == nil {
				entries := []string{}
				for _, entry := range result.Resources {
					data, encodeError := proto.Marshal(entry)
					if encodeError != nil {
						return observed, encodeError
					}
					entries = append(entries, base64.StdEncoding.EncodeToString(data))
				}
				observed.Resources = append(observed.Resources, entries)
			}
		case "receive-error":
			connection.latest().receiveFailure = status.Error(codes.Unavailable, "fixture receive failure")
			_, err = ads.Fetch()
		case "eof":
			connection.latest().receiveFailure = io.EOF
			_, err = ads.Fetch()
		case "fetch":
			_, err = ads.Fetch()
		case "send-error-ack":
			connection.latest().sendFailure = errAdsSend
			err = ads.Ack()
		case "ack":
			err = ads.Ack()
		case "nack":
			err = ads.Nack("rejected")
		default:
			return observed, fmt.Errorf("unknown client fixture operation %q", step.Operation)
		}
		if err != nil {
			kind := ""
			switch {
			case errors.Is(err, client.ErrInit):
				kind = "init"
			case errors.Is(err, errAdsSend):
				kind = "send"
			case errors.Is(err, errAdsOpen):
				kind = "open"
			case errors.Is(err, io.EOF):
				kind = "eof"
			case status.Code(err) == codes.Unavailable:
				kind = "unavailable"
			default:
				return observed, err
			}
			observed.Errors = append(observed.Errors, adsClientError{Kind: kind, Connection: client.IsConnError(err)})
		}
	}
	for index, call := range connection.calls {
		for _, request := range call.requests {
			if request.GetNode().GetId() != "node" || request.TypeUrl != resource.RouteType || len(request.ResourceNames) != 0 {
				return observed, fmt.Errorf("client changed fixed node/type/subscription")
			}
			var errorMessage *string
			if request.ErrorDetail != nil {
				message := request.ErrorDetail.Message
				errorMessage = &message
			}
			observed.Requests = append(observed.Requests, adsClientRequest{Version: request.VersionInfo, Nonce: request.ResponseNonce,
				Error: errorMessage, Call: index + 1, Code: int(request.GetErrorDetail().GetCode())})
		}
	}
	return observed, nil
}

type scriptedAdsConnection struct {
	calls                        []*scriptedAdsStream
	openFailure, nextSendFailure error
}

func (*scriptedAdsConnection) Invoke(context.Context, string, any, any, ...grpc.CallOption) error {
	return errors.New("unexpected unary call")
}
func (connection *scriptedAdsConnection) NewStream(ctx context.Context, _ *grpc.StreamDesc, method string, _ ...grpc.CallOption) (grpc.ClientStream, error) {
	if method != "/envoy.service.discovery.v3.AggregatedDiscoveryService/StreamAggregatedResources" {
		return nil, fmt.Errorf("unexpected method %s", method)
	}
	if connection.openFailure != nil {
		err := connection.openFailure
		connection.openFailure = nil
		return nil, err
	}
	stream := &scriptedAdsStream{ctx: ctx, sendFailure: connection.nextSendFailure}
	connection.nextSendFailure = nil
	connection.calls = append(connection.calls, stream)
	return stream, nil
}
func (connection *scriptedAdsConnection) latest() *scriptedAdsStream {
	return connection.calls[len(connection.calls)-1]
}

type scriptedAdsStream struct {
	ctx                         context.Context
	requests                    []*discovery.DiscoveryRequest
	response                    *discovery.DiscoveryResponse
	sendFailure, receiveFailure error
}

func (*scriptedAdsStream) Header() (metadata.MD, error)    { return metadata.MD{}, nil }
func (*scriptedAdsStream) Trailer() metadata.MD            { return metadata.MD{} }
func (*scriptedAdsStream) CloseSend() error                { return nil }
func (stream *scriptedAdsStream) Context() context.Context { return stream.ctx }
func (stream *scriptedAdsStream) SendMsg(message any) error {
	stream.requests = append(stream.requests, proto.Clone(message.(*discovery.DiscoveryRequest)).(*discovery.DiscoveryRequest))
	err := stream.sendFailure
	stream.sendFailure = nil
	return err
}
func (stream *scriptedAdsStream) RecvMsg(message any) error {
	if stream.receiveFailure != nil {
		err := stream.receiveFailure
		stream.receiveFailure = nil
		return err
	}
	if stream.response == nil {
		return errors.New("fixture expected a supplied response")
	}
	proto.Merge(message.(proto.Message), stream.response)
	stream.response = nil
	return nil
}
