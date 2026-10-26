package main

import (
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"os"
	"reflect"
	"sort"
	"time"

	"github.com/envoyproxy/go-control-plane/pkg/cache/types"
	cache "github.com/envoyproxy/go-control-plane/pkg/cache/v3"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protoregistry"
)

type wireEntry struct {
	Data           string
	TtlNanoseconds *int64
}

type wireGroup struct {
	TypeURL string `json:"TypeUrl"`
	Entries []wireEntry
}

type snapshotCase struct {
	ID            string `json:"Id"`
	Version       string
	Groups        []wireGroup
	Consistent    bool
	ResourceNames map[string][]string
	Versions      map[string]string
	Ttls          map[string]map[string]*int64
}

type result struct {
	ID            string              `json:"id"`
	Consistent    bool                `json:"consistent"`
	ResourceNames map[string][]string `json:"resourceNames"`
}

func main() {
	var err error
	if len(os.Args) > 1 && os.Args[1] == "cache" {
		err = runCache(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "subscription" {
		err = runSubscriptions(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "delta-subscription" {
		err = runDeltaSubscriptions(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "delta-cache" {
		err = runDeltaCache(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "linear-cache" {
		err = runLinearCache(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "mux-cache" {
		err = runMuxCache(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "heartbeats" {
		err = runHeartbeats(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "fetch" {
		err = runFetch(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "gateway" {
		err = runGateway(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "ads-client" {
		err = runAdsClient(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "delta-stream" {
		err = runDeltaStreams(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "stream" {
		err = runStreams(os.Args[2:])
	} else if len(os.Args) > 1 && os.Args[1] == "helpers" {
		err = runHelpers(os.Args[2:])
	} else {
		err = run()
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run() error {
	if len(os.Args) != 3 {
		return fmt.Errorf("usage: resource-oracle <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(os.Args[1])
	if err != nil {
		return err
	}
	var cases []snapshotCase
	if err := json.Unmarshal(input, &cases); err != nil {
		return err
	}
	if len(cases) != 19 {
		return fmt.Errorf("expected 19 declared snapshot cases, got %d", len(cases))
	}
	if _, err := cache.NewSnapshot("v1", map[string][]types.Resource{"custom/type": nil}); err == nil {
		return fmt.Errorf("upstream unexpectedly accepted an unknown default snapshot type")
	}
	seen := make(map[string]bool)
	results := make([]result, 0, len(cases))
	for _, fixture := range cases {
		if seen[fixture.ID] {
			return fmt.Errorf("duplicate case %s", fixture.ID)
		}
		seen[fixture.ID] = true
		row, err := compare(fixture)
		if err != nil {
			return fmt.Errorf("%s: %w", fixture.ID, err)
		}
		results = append(results, row)
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(os.Args[2], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go snapshots: %d cases matched names, versions, TTLs, consistency and lazy version-map outcomes.\n", len(results))
	return nil
}

func decodeResourceGroups(wireGroups []wireGroup) (map[string][]types.ResourceWithTTL, error) {
	groups := make(map[string][]types.ResourceWithTTL)
	for _, group := range wireGroups {
		entries := make([]types.ResourceWithTTL, 0, len(group.Entries))
		for _, entry := range group.Entries {
			messageType, err := protoregistry.GlobalTypes.FindMessageByURL(group.TypeURL)
			if err != nil {
				return nil, err
			}
			message := messageType.New().Interface()
			bytes, err := base64.StdEncoding.DecodeString(entry.Data)
			if err != nil {
				return nil, err
			}
			if err := proto.Unmarshal(bytes, message); err != nil {
				return nil, err
			}
			resource := types.ResourceWithTTL{Resource: message}
			if entry.TtlNanoseconds != nil {
				ttl := time.Duration(*entry.TtlNanoseconds)
				resource.TTL = &ttl
			}
			entries = append(entries, resource)
		}
		groups[group.TypeURL] = entries
	}
	return groups, nil
}

func compare(fixture snapshotCase) (result, error) {
	groups, err := decodeResourceGroups(fixture.Groups)
	if err != nil {
		return result{}, err
	}
	snapshot, err := cache.NewSnapshotWithTTLs(fixture.Version, groups)
	if err != nil {
		return result{}, err
	}
	consistent := snapshot.Consistent() == nil
	if consistent != fixture.Consistent {
		return result{}, fmt.Errorf("consistency differs: Go=%t .NET=%t", consistent, fixture.Consistent)
	}
	names := make(map[string][]string)
	versions := make(map[string]string)
	ttls := make(map[string]map[string]*int64)
	for typeURL := range groups {
		if snapshot.GetVersionMap(typeURL) != nil {
			return result{}, fmt.Errorf("version map was constructed eagerly")
		}
		items := snapshot.GetResourcesAndTTL(typeURL)
		names[typeURL] = make([]string, 0, len(items))
		ttls[typeURL] = make(map[string]*int64)
		for name, entry := range items {
			names[typeURL] = append(names[typeURL], name)
			ttls[typeURL][name] = nil
			if entry.TTL != nil {
				nanos := entry.TTL.Nanoseconds()
				ttls[typeURL][name] = &nanos
			}
		}
		sort.Strings(names[typeURL])
		versions[typeURL] = snapshot.GetVersion(typeURL)
	}
	if !reflect.DeepEqual(names, fixture.ResourceNames) {
		return result{}, fmt.Errorf("resource names differ")
	}
	if !reflect.DeepEqual(versions, fixture.Versions) {
		return result{}, fmt.Errorf("opaque versions differ")
	}
	if !reflect.DeepEqual(ttls, fixture.Ttls) {
		return result{}, fmt.Errorf("TTL values or presence differ")
	}
	if err := snapshot.ConstructVersionMap(); err != nil {
		return result{}, err
	}
	for typeURL := range groups {
		hashes := snapshot.GetVersionMap(typeURL)
		if len(hashes) != len(names[typeURL]) {
			return result{}, fmt.Errorf("version-map size differs")
		}
		for _, hash := range hashes {
			bytes, err := hex.DecodeString(hash)
			if err != nil || len(bytes) != 32 {
				return result{}, fmt.Errorf("invalid SHA256 resource version")
			}
		}
	}
	return result{ID: fixture.ID, Consistent: consistent, ResourceNames: names}, nil
}
