package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"
	"reflect"
	"sort"

	listener "github.com/envoyproxy/go-control-plane/envoy/config/listener/v3"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	resource "github.com/envoyproxy/go-control-plane/pkg/resource/v3"
	"github.com/envoyproxy/go-control-plane/pkg/wellknown"
	"google.golang.org/protobuf/proto"
)

type helperCase struct {
	ID         string
	FilterData *string
	IsHcm      bool
	RouteName  *string
}

type helperReport struct {
	Labels     map[string]string
	APIVersion int
	Cases      []helperCase
	Graph      snapshotCase
	References map[string][]string
}

func runHelpers(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle helpers <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var report helperReport
	if err := json.Unmarshal(input, &report); err != nil {
		return err
	}
	expectedLabels := map[string]string{"httpConnectionManager": wellknown.HTTPConnectionManager, "router": wellknown.Router}
	if !reflect.DeepEqual(expectedLabels, report.Labels) || int(resource.DefaultAPIVersion) != report.APIVersion {
		return fmt.Errorf("preview labels or API version differ from upstream")
	}
	if len(report.Cases) != 8 {
		return fmt.Errorf("expected eight declared helper cases")
	}
	seen := map[string]bool{}
	for _, fixture := range report.Cases {
		if seen[fixture.ID] {
			return fmt.Errorf("duplicate helper case %s", fixture.ID)
		}
		seen[fixture.ID] = true
		var filter *listener.Filter
		if fixture.FilterData != nil {
			payload, err := base64.StdEncoding.DecodeString(*fixture.FilterData)
			if err != nil {
				return err
			}
			filter = &listener.Filter{}
			if err := proto.Unmarshal(payload, filter); err != nil {
				return err
			}
		}
		manager := resource.GetHTTPConnectionManager(filter)
		var routeName *string
		if manager != nil && manager.GetRds() != nil {
			name := manager.GetRds().GetRouteConfigName()
			routeName = &name
		}
		if (manager != nil) != fixture.IsHcm || !reflect.DeepEqual(routeName, fixture.RouteName) {
			return fmt.Errorf("%s: HCM extraction differs", fixture.ID)
		}
	}
	if _, err := compare(report.Graph); err != nil {
		return fmt.Errorf("preview graph: %w", err)
	}
	groups, err := decodeResourceGroups(report.Graph.Groups)
	if err != nil {
		return err
	}
	snapshot, err := cache.NewSnapshotWithTTLs(report.Graph.Version, groups)
	if err != nil {
		return err
	}
	references := map[string][]string{}
	for typeURL, names := range cache.GetAllResourceReferences(snapshot.Resources) {
		references[typeURL] = make([]string, 0, len(names))
		for name := range names {
			references[typeURL] = append(references[typeURL], name)
		}
		sort.Strings(references[typeURL])
	}
	if !reflect.DeepEqual(references, report.References) {
		return fmt.Errorf("preview graph references differ")
	}
	output, err := json.MarshalIndent(report, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go preview helpers: eight HCM/Any cases, two filter labels, API version and the complete example graph matched.\n")
	return nil
}
