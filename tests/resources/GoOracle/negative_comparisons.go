package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

type negativeComparisonInput struct{ Mode, Report string }
type negativeComparisonResult struct {
	Mode       string `json:"mode"`
	Case       string `json:"case"`
	Mutation   string `json:"mutation"`
	Diagnostic string `json:"diagnostic"`
	Rejected   bool   `json:"rejected"`
}

func runNegativeComparisons(args []string) error {
	if len(args) != 2 {
		return fmt.Errorf("usage: resource-oracle negative-comparisons <manifest-json> <output-json>")
	}
	data, err := os.ReadFile(args[0])
	if err != nil {
		return err
	}
	var inputs []negativeComparisonInput
	if err := json.Unmarshal(data, &inputs); err != nil {
		return err
	}
	runners := map[string]func([]string) error{
		"snapshot": runSnapshots, "cache": runCache, "subscription": runSubscriptions, "helpers": runHelpers,
		"delta-subscription": runDeltaSubscriptions, "delta-cache": runDeltaCache,
		"linear-cache": runLinearCache, "mux-cache": runMuxCache, "heartbeats": runHeartbeats,
		"fetch": runFetch, "fetch-engine": runFetchEngine, "gateway": runGateway,
		"ads-client": runAdsClient, "conversions": runConversions, "helper-symbols": runConversions,
	}
	if len(inputs) != len(runners) {
		return fmt.Errorf("negative comparison manifest must cover all %d modes", len(runners))
	}
	directory := filepath.Join(filepath.Dir(args[1]), "negative-inputs")
	if err := os.MkdirAll(directory, 0o755); err != nil {
		return err
	}
	results := []negativeComparisonResult{}
	seen := make(map[string]bool)
	for _, input := range inputs {
		runner, exists := runners[input.Mode]
		if !exists || seen[input.Mode] {
			return fmt.Errorf("unknown/duplicate negative comparison mode %s", input.Mode)
		}
		seen[input.Mode] = true
		data, err := os.ReadFile(input.Report)
		if err != nil {
			return err
		}
		var document any
		if err := json.Unmarshal(data, &document); err != nil {
			return err
		}
		id, mutation, marker := corruptComparison(input.Mode, document)
		changed, err := json.MarshalIndent(document, "", "  ")
		if err != nil {
			return err
		}
		candidate := filepath.Join(directory, input.Mode+".json")
		if err := os.WriteFile(candidate, append(changed, '\n'), 0o644); err != nil {
			return err
		}
		err = runner([]string{candidate, filepath.Join(directory, input.Mode+"-unexpected-output.json")})
		if err == nil {
			return fmt.Errorf("%s comparer accepted %s", input.Mode, mutation)
		}
		if !strings.Contains(err.Error(), marker) {
			return fmt.Errorf("%s negative check did not reach the expected comparer failure %q: %w", input.Mode, marker, err)
		}
		results = append(results, negativeComparisonResult{Mode: input.Mode, Case: id, Mutation: mutation, Diagnostic: err.Error(), Rejected: true})
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(args[1], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go negative comparers: %d deliberate semantic corruptions rejected by their exact comparison diagnostics.\n", len(results))
	return nil
}

// These mutate only recorded outcomes, never prescribed scenario inputs. The
// existing runners still call the actual pinned library before rejecting them.
func corruptComparison(mode string, document any) (id, mutation, marker string) {
	if mode == "conversions" {
		first := document.(map[string]any)["cases"].([]any)[0].(map[string]any)
		id = comparisonCaseID(first["input"].(map[string]any))
		first["observed"].(map[string]any)["struct"].(map[string]any)["version_info"] = "corrupt-version"
		return id, "Struct field value", id + ": conversion differs"
	}
	if mode == "helper-symbols" {
		first := document.(map[string]any)["symbols"].([]any)[0].(map[string]any)
		id = first["source"].(string)
		first["value"] = "corrupt-label"
		return id, "conventional label", "helper symbol " + id + " differs"
	}
	if mode == "helpers" {
		first := document.(map[string]any)["Cases"].([]any)[0].(map[string]any)
		id = comparisonCaseID(first)
		first["IsHcm"] = !first["IsHcm"].(bool)
		return id, "HCM extraction result", id + ": HCM extraction differs"
	}
	rows := document.([]any)
	first := rows[0].(map[string]any)
	if mode == "ads-client" {
		for _, row := range rows {
			candidate := row.(map[string]any)
			if comparisonCaseID(candidate["input"].(map[string]any)) == "ack-flow" {
				first = candidate
				break
			}
		}
	}
	if input, exists := first["input"]; exists {
		id = comparisonCaseID(input.(map[string]any))
	} else {
		id = comparisonCaseID(first)
	}
	if mode == "snapshot" {
		first["Consistent"] = !first["Consistent"].(bool)
		return id, "snapshot consistency", id + ": consistency differs"
	}
	if mode == "cache" {
		first["observed"].([]any)[0].(map[string]any)["state"] = "corrupt-state"
		return id, "watch lifecycle state", id + ": cache trace differs"
	}
	if mode == "subscription" || mode == "delta-subscription" {
		state := first["observed"].([]any)[0].(map[string]any)
		state["wildcard"] = !state["wildcard"].(bool)
		label := "SotW state differs"
		if mode == "delta-subscription" {
			label = "Delta subscription state differs"
		}
		return id, "subscription wildcard", id + ": " + label
	}
	observed := first["observed"].(map[string]any)
	switch mode {
	case "delta-cache":
		observed["watches"] = observed["watches"].(float64) + 1
		return id, "retained watch count", id + ": Delta cache differs"
	case "linear-cache":
		observed["response"].(map[string]any)["version"] = "corrupt-version"
		return id, "collection version", id + ": LinearCache differs"
	case "mux-cache":
		observed["sotwClassifications"] = observed["sotwClassifications"].(float64) + 1
		return id, "classifier call count", id + ": MuxCache differs"
	case "heartbeats":
		entry := observed["response"].(map[string]any)["resources"].([]any)[0].(map[string]any)
		entry["hasPayload"] = !entry["hasPayload"].(bool)
		return id, "normal TTL payload presence", id + ": heartbeat differs"
	case "fetch":
		observed["version"] = "corrupt-version"
		return id, "Fetch version", id + ": Fetch differs"
	case "fetch-engine":
		events := observed["events"].([]any)
		events[2], events[3] = events[3], events[2]
		return id, "response callback before materialization", id + ": Fetch engine differs"
	case "gateway":
		observed["status"] = 304
		return id, "HTTP status", id + ": gateway differs"
	case "ads-client":
		observed["requests"].([]any)[1].(map[string]any)["version"] = "corrupt-version"
		return id, "ACK accepted version", id + ": ADS client differs"
	default:
		panic("unhandled comparison corruption: " + mode)
	}
}

func comparisonCaseID(row map[string]any) string {
	if value, exists := row["id"]; exists {
		return value.(string)
	}
	return row["Id"].(string)
}
