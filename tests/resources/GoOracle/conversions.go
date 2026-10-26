// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Calls the pinned conversion functions and compares their public symbol inventory.
package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"
	"reflect"

	"github.com/envoyproxy/go-control-plane/pkg/conversion"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	"github.com/envoyproxy/go-control-plane/pkg/wellknown"
	"google.golang.org/protobuf/encoding/protojson"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protoreflect"
	"google.golang.org/protobuf/reflect/protoregistry"
	"google.golang.org/protobuf/types/known/structpb"
)

func helperSymbolValues() map[string]string {
	return map[string]string{
		"wellknown.Buffer":                     wellknown.Buffer,
		"wellknown.CORS":                       wellknown.CORS,
		"wellknown.Dynamo":                     wellknown.Dynamo,
		"wellknown.Fault":                      wellknown.Fault,
		"wellknown.GRPCHTTP1Bridge":            wellknown.GRPCHTTP1Bridge,
		"wellknown.GRPCJSONTranscoder":         wellknown.GRPCJSONTranscoder,
		"wellknown.GRPCWeb":                    wellknown.GRPCWeb,
		"wellknown.Gzip":                       wellknown.Gzip,
		"wellknown.IPTagging":                  wellknown.IPTagging,
		"wellknown.HTTPRateLimit":              wellknown.HTTPRateLimit,
		"wellknown.Router":                     wellknown.Router,
		"wellknown.HealthCheck":                wellknown.HealthCheck,
		"wellknown.Lua":                        wellknown.Lua,
		"wellknown.Squash":                     wellknown.Squash,
		"wellknown.HTTPExternalAuthorization":  wellknown.HTTPExternalAuthorization,
		"wellknown.HTTPRoleBasedAccessControl": wellknown.HTTPRoleBasedAccessControl,
		"wellknown.HTTPGRPCStats":              wellknown.HTTPGRPCStats,
		"wellknown.HTTPWasm":                   wellknown.HTTPWasm,
		"wellknown.ClientSSLAuth":              wellknown.ClientSSLAuth,
		"wellknown.Echo":                       wellknown.Echo,
		"wellknown.HTTPConnectionManager":      wellknown.HTTPConnectionManager,
		"wellknown.TCPProxy":                   wellknown.TCPProxy,
		"wellknown.RateLimit":                  wellknown.RateLimit,
		"wellknown.MongoProxy":                 wellknown.MongoProxy,
		"wellknown.ThriftProxy":                wellknown.ThriftProxy,
		"wellknown.RedisProxy":                 wellknown.RedisProxy,
		"wellknown.MySQLProxy":                 wellknown.MySQLProxy,
		"wellknown.ExternalAuthorization":      wellknown.ExternalAuthorization,
		"wellknown.RoleBasedAccessControl":     wellknown.RoleBasedAccessControl,
		"wellknown.OriginalDestination":        wellknown.OriginalDestination,
		"wellknown.ProxyProtocol":              wellknown.ProxyProtocol,
		"wellknown.TlsInspector":               wellknown.TlsInspector,
		"wellknown.TLSInspector":               wellknown.TLSInspector,
		"wellknown.HttpInspector":              wellknown.HttpInspector,
		"wellknown.HTTPInspector":              wellknown.HTTPInspector,
		"wellknown.OriginalSource":             wellknown.OriginalSource,
		"wellknown.Lightstep":                  wellknown.Lightstep,
		"wellknown.Zipkin":                     wellknown.Zipkin,
		"wellknown.DynamicOT":                  wellknown.DynamicOT,
		"wellknown.Datadog":                    wellknown.Datadog,
		"wellknown.Statsd":                     wellknown.Statsd,
		"wellknown.DogStatsd":                  wellknown.DogStatsd,
		"wellknown.MetricsService":             wellknown.MetricsService,
		"wellknown.FileAccessLog":              wellknown.FileAccessLog,
		"wellknown.HTTPGRPCAccessLog":          wellknown.HTTPGRPCAccessLog,
		"wellknown.TransportSocketAlts":        wellknown.TransportSocketAlts,
		"wellknown.TransportSocketTap":         wellknown.TransportSocketTap,
		"wellknown.TransportSocketRawBuffer":   wellknown.TransportSocketRawBuffer,
		"wellknown.TransportSocketTls":         wellknown.TransportSocketTls,
		"wellknown.TransportSocketTLS":         wellknown.TransportSocketTLS,
		"wellknown.TransportSocketQuic":        wellknown.TransportSocketQuic,
		"resource.APITypePrefix":               resource.APITypePrefix,
		"resource.EndpointType":                resource.EndpointType,
		"resource.ClusterType":                 resource.ClusterType,
		"resource.RouteType":                   resource.RouteType,
		"resource.ScopedRouteType":             resource.ScopedRouteType,
		"resource.VirtualHostType":             resource.VirtualHostType,
		"resource.ListenerType":                resource.ListenerType,
		"resource.FilterChainType":             resource.FilterChainType,
		"resource.SecretType":                  resource.SecretType,
		"resource.ExtensionConfigType":         resource.ExtensionConfigType,
		"resource.RuntimeType":                 resource.RuntimeType,
		"resource.ThriftRouteType":             resource.ThriftRouteType,
		"resource.RateLimitConfigType":         resource.RateLimitConfigType,
		"resource.LbEndpointType":              resource.LbEndpointType,
		"resource.AnyType":                     resource.AnyType,
		"resource.FetchEndpoints":              resource.FetchEndpoints,
		"resource.FetchClusters":               resource.FetchClusters,
		"resource.FetchListeners":              resource.FetchListeners,
		"resource.FetchRoutes":                 resource.FetchRoutes,
		"resource.FetchScopedRoutes":           resource.FetchScopedRoutes,
		"resource.FetchSecrets":                resource.FetchSecrets,
		"resource.FetchRuntimes":               resource.FetchRuntimes,
		"resource.FetchExtensionConfigs":       resource.FetchExtensionConfigs,
	}
}

type conversionInput struct {
	ID, Mode, TypeName, Data string
	StructJSON               *string
	NullInput                bool
}
type conversionObservation struct {
	Error   string
	Struct  any
	Message *string
}
type conversionCase struct {
	Input    conversionInput
	Observed conversionObservation
}
type helperSymbol struct {
	Source, Target, Value string
}
type conversionReport struct {
	APIVersion int
	Symbols    []helperSymbol
	Cases      []conversionCase
}

func runConversions(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle conversions <candidate-report-json> <output-json>")
	}
	data, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var report conversionReport
	if err := json.Unmarshal(data, &report); err != nil {
		return err
	}
	symbols := helperSymbolValues()
	if len(symbols) != 74 || len(report.Symbols) != len(symbols) || report.APIVersion != int(resource.DefaultAPIVersion) {
		return fmt.Errorf("helper symbol inventory or API version differs")
	}
	seen := make(map[string]bool)
	for _, symbol := range report.Symbols {
		value, exists := symbols[symbol.Source]
		if !exists || seen[symbol.Source] || value != symbol.Value {
			return fmt.Errorf("helper symbol %s differs or is duplicated", symbol.Source)
		}
		seen[symbol.Source] = true
	}
	if len(report.Cases) != 17 {
		return fmt.Errorf("expected seventeen conversion cases, got %d", len(report.Cases))
	}
	seen = make(map[string]bool)
	for index, fixture := range report.Cases {
		if seen[fixture.Input.ID] {
			return fmt.Errorf("duplicate conversion case %s", fixture.Input.ID)
		}
		seen[fixture.Input.ID] = true
		actual, err := observeConversion(fixture.Input)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.Input.ID, err)
		}
		if err := compareConversion(fixture.Input.TypeName, actual, fixture.Observed); err != nil {
			return fmt.Errorf("%s: conversion differs: %w", fixture.Input.ID, err)
		}
		report.Cases[index].Observed = actual
	}
	output, err := json.MarshalIndent(report, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go helpers: %d constants + API version and %d conversion cases matched proto JSON/Struct/message semantics.\n", len(symbols), len(report.Cases))
	return nil
}

func observeConversion(input conversionInput) (conversionObservation, error) {
	observed := conversionObservation{}
	messageType, err := protoregistry.GlobalTypes.FindMessageByName(protoreflect.FullName(input.TypeName))
	if err != nil {
		return observed, err
	}
	var value *structpb.Struct
	if input.Mode == "from" {
		if !input.NullInput {
			value = &structpb.Struct{}
			if err := protojson.Unmarshal([]byte(*input.StructJSON), value); err != nil {
				return observed, err
			}
		}
	} else {
		var message proto.Message
		if !input.NullInput {
			message = messageType.New().Interface()
			data, err := base64.StdEncoding.DecodeString(input.Data)
			if err != nil {
				return observed, err
			}
			if err := proto.Unmarshal(data, message); err != nil {
				return observed, err
			}
		}
		value, err = conversion.MessageToStruct(message)
		if err != nil {
			observed.Error = "to-struct"
			return observed, nil
		}
		data, err := protojson.Marshal(value)
		if err != nil {
			return observed, err
		}
		if err := json.Unmarshal(data, &observed.Struct); err != nil {
			return observed, err
		}
	}
	out := messageType.New().Interface()
	if err := conversion.StructToMessage(value, out); err != nil {
		observed.Error = "to-message"
		return observed, nil
	}
	data, err := (proto.MarshalOptions{Deterministic: true}).Marshal(out)
	if err != nil {
		return observed, err
	}
	encoded := base64.StdEncoding.EncodeToString(data)
	observed.Message = &encoded
	return observed, nil
}

func compareConversion(typeName string, actual, candidate conversionObservation) error {
	if actual.Error != candidate.Error || !reflect.DeepEqual(actual.Struct, candidate.Struct) || (actual.Message == nil) != (candidate.Message == nil) {
		return fmt.Errorf("JSON, error stage or message presence mismatch; Go=%+v .NET=%+v", actual, candidate)
	}
	if actual.Message == nil {
		return nil
	}
	messageType, err := protoregistry.GlobalTypes.FindMessageByName(protoreflect.FullName(typeName))
	if err != nil {
		return err
	}
	goMessage, netMessage := messageType.New().Interface(), messageType.New().Interface()
	for _, entry := range []struct {
		encoded string
		message proto.Message
	}{{*actual.Message, goMessage}, {*candidate.Message, netMessage}} {
		data, err := base64.StdEncoding.DecodeString(entry.encoded)
		if err != nil {
			return err
		}
		if err := proto.Unmarshal(data, entry.message); err != nil {
			return err
		}
	}
	if !proto.Equal(goMessage, netMessage) {
		return fmt.Errorf("converted protobuf messages differ")
	}
	return nil
}
