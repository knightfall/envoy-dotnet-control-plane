package main

import (
	"encoding/json"
	"fmt"
	"os"
	"reflect"

	stream "github.com/envoyproxy/go-control-plane/pkg/server/stream/v3"
)

type deltaSubscriptionStep struct {
	Kind         string
	Subscribed   []string
	Unsubscribed []string
	Returned     map[string]string
}

type deltaSubscriptionCase struct {
	ID                  string `json:"id"`
	AllowLegacyWildcard bool
	InitialSubscribed   []string
	InitialUnsubscribed []string
	InitialVersions     map[string]string
	Steps               []deltaSubscriptionStep
	Observed            []sotwObservation
}

func runDeltaSubscriptions(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle delta-subscription <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var cases []deltaSubscriptionCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 12 {
		return fmt.Errorf("expected twelve Delta subscription traces, got %d", len(cases))
	}
	results := make([]sotwCaseResult, 0, len(cases))
	seen := make(map[string]bool)
	for _, fixture := range cases {
		if seen[fixture.ID] {
			return fmt.Errorf("duplicate Delta subscription trace %s", fixture.ID)
		}
		seen[fixture.ID] = true
		subscription := stream.NewDeltaSubscription(fixture.InitialSubscribed, fixture.InitialUnsubscribed, fixture.InitialVersions, fixture.AllowLegacyWildcard)
		observed := []sotwObservation{observeSubscription(subscription)}
		for _, step := range fixture.Steps {
			switch step.Kind {
			case "update":
				subscription.UpdateResourceSubscriptions(step.Subscribed, step.Unsubscribed)
			case "returned":
				subscription.SetReturnedResources(step.Returned)
			case "reconnect":
				subscription = stream.NewDeltaSubscription(step.Subscribed, step.Unsubscribed, step.Returned, fixture.AllowLegacyWildcard)
			default:
				return fmt.Errorf("%s: unknown Delta subscription step %s", fixture.ID, step.Kind)
			}
			observed = append(observed, observeSubscription(subscription))
		}
		if !reflect.DeepEqual(observed, fixture.Observed) {
			actual, _ := json.Marshal(observed)
			expected, _ := json.Marshal(fixture.Observed)
			return fmt.Errorf("%s: Delta subscription state differs\nGo: %s\n.NET: %s", fixture.ID, actual, expected)
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
	fmt.Printf("Go Delta subscriptions: %d traces matched wildcard, exact/prefix sets and resource versions.\n", len(results))
	return nil
}
