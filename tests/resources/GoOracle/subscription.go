package main

import (
	"encoding/json"
	"fmt"
	"os"
	"reflect"
	"sort"

	"github.com/envoyproxy/go-control-plane/pkg/server/config"
	stream "github.com/envoyproxy/go-control-plane/pkg/server/stream/v3"
)

type sotwStep struct {
	Kind     string
	Names    []string
	Returned map[string]string
}

type sotwObservation struct {
	Wildcard bool              `json:"wildcard"`
	Exact    []string          `json:"exact"`
	Prefixes []string          `json:"prefixes"`
	Returned map[string]string `json:"returned"`
}

type sotwCase struct {
	ID            string `json:"id"`
	TypeURL       string `json:"typeUrl"`
	DisableAll    bool
	DisabledTypes []string
	Initial       []string
	Steps         []sotwStep
	Observed      []sotwObservation
}

type sotwCaseResult struct {
	ID       string            `json:"id"`
	Observed []sotwObservation `json:"observed"`
}

func runSubscriptions(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle subscription <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []sotwCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 14 {
		return fmt.Errorf("expected 14 SotW traces, got %d", len(cases))
	}
	results := make([]sotwCaseResult, 0, len(cases))
	seen := make(map[string]bool)
	for _, fixture := range cases {
		if seen[fixture.ID] {
			return fmt.Errorf("duplicate SotW trace %s", fixture.ID)
		}
		seen[fixture.ID] = true
		options := config.NewOpts()
		config.DeactivateLegacyWildcardForTypes(fixture.DisabledTypes)(&options)
		if fixture.DisableAll {
			config.DeactivateLegacyWildcard()(&options)
		}
		subscription := stream.NewSotwSubscription(fixture.Initial, options.IsLegacyWildcardActive(fixture.TypeURL))
		observed := []sotwObservation{observeSubscription(subscription)}
		for _, step := range fixture.Steps {
			switch step.Kind {
			case "subscribe":
				subscription.SetResourceSubscription(step.Names)
			case "returned":
				subscription.SetReturnedResources(step.Returned)
			default:
				return fmt.Errorf("%s: unknown SotW step %s", fixture.ID, step.Kind)
			}
			observed = append(observed, observeSubscription(subscription))
		}
		if !reflect.DeepEqual(observed, fixture.Observed) {
			actual, _ := json.Marshal(observed)
			expected, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: SotW state differs\nGo: %s\n.NET: %s", fixture.ID, actual, expected)
		}
		results = append(results, sotwCaseResult{ID: fixture.ID, Observed: observed})
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go SotW subscriptions: %d traces matched wildcard, exact/prefix sets and returned versions.\n", len(results))
	return nil
}

func observeSubscription(subscription stream.Subscription) sotwObservation {
	exact := make([]string, 0, len(subscription.SubscribedResources()))
	for name := range subscription.SubscribedResources() {
		exact = append(exact, name)
	}
	prefixes := make([]string, 0, len(subscription.SubscribedPrefixes()))
	for prefix := range subscription.SubscribedPrefixes() {
		prefixes = append(prefixes, prefix)
	}
	sort.Strings(exact)
	sort.Strings(prefixes)
	returned := make(map[string]string)
	for name, version := range subscription.ReturnedResources() {
		returned[name] = version
	}
	return sotwObservation{Wildcard: subscription.IsWildcard(), Exact: exact, Prefixes: prefixes, Returned: returned}
}
