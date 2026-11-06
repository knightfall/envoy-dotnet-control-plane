package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"
	"strings"

	_ "github.com/cncf/xds/go/xds/core/v3"
	_ "github.com/cncf/xds/go/xds/type/matcher/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/accesslog/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/bootstrap/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/cluster/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/listener/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/metrics/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/route/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/config/tap/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/data/accesslog/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/data/dns/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/access_loggers/file/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/access_loggers/stats/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/config/validators/dynamic_modules/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/common/fault/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/common/jwks/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/api_key_auth/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/buffer/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/compressor/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/ext_authz/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/file_server/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/gcp_authn/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/lua/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/http/thrift_to_metadata/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/network/redis_proxy/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/filters/network/reverse_tunnel/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/http/original_ip_detection/custom_header/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/load_balancing_policies/client_side_weighted_round_robin/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/load_balancing_policies/load_aware_locality/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/load_balancing_policies/ring_hash/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/resource_monitors/downstream_connections/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/retry/priority/previous_priorities/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/router/cluster_specifiers/priority_group/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/transport_sockets/tls/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/service/rate_limit_quota/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/type/http/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/type/v3"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protoreflect"
	"google.golang.org/protobuf/reflect/protoregistry"
)

type validationCase struct {
	ID              string `json:"Id"`
	Field           string `json:"Field"`
	Type            string `json:"Type"`
	Data            string `json:"Data"`
	ExpectedValid   bool   `json:"ExpectedValid"`
	ExpectedGoValid *bool  `json:"ExpectedGoValid"`
}

type result struct {
	ID    string `json:"id"`
	Valid bool   `json:"valid"`
	Error string `json:"error,omitempty"`
}

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run() error {
	if len(os.Args) != 3 {
		return fmt.Errorf("usage: go-oracle <candidate-report-json> <output-json>")
	}
	input, err := os.ReadFile(os.Args[1])
	if err != nil {
		return err
	}
	var report struct{ Results []validationCase }
	if err := json.Unmarshal(input, &report); err != nil {
		return err
	}
	if len(report.Results) == 0 {
		return fmt.Errorf("empty corpus")
	}

	results := make([]result, 0, len(report.Results))
	for _, item := range report.Results {
		messageType, err := protoregistry.GlobalTypes.FindMessageByName(protoreflect.FullName(item.Type))
		if err != nil {
			return err
		}
		data, err := base64.StdEncoding.DecodeString(item.Data)
		if err != nil {
			return err
		}
		message := messageType.New().Interface()
		if err := proto.Unmarshal(data, message); err != nil {
			return err
		}
		validator, ok := message.(interface{ ValidateAll() error })
		if !ok {
			return fmt.Errorf("%s has no generated PGV validator", item.Type)
		}
		validationError := validator.ValidateAll()
		actual := result{ID: item.ID, Valid: validationError == nil}
		if item.Field != "" {
			rejected, err := fieldRejected(validationError, item.Field)
			if err != nil {
				return fmt.Errorf("%s: %w", item.ID, err)
			}
			actual.Valid = !rejected
		}
		if validationError != nil {
			actual.Error = validationError.Error()
		}
		results = append(results, actual)
		expected := item.ExpectedValid
		if item.ExpectedGoValid != nil {
			expected = *item.ExpectedGoValid
		}
		if actual.Valid != expected {
			return fmt.Errorf("%s: expected valid=%t, got valid=%t: %s", item.ID, expected, actual.Valid, actual.Error)
		}
	}
	output, err := json.MarshalIndent(results, "", "  ")
	if err != nil {
		return err
	}
	if err := os.WriteFile(os.Args[2], append(output, '\n'), 0o644); err != nil {
		return err
	}
	fmt.Printf("Go PGV: %d cases matched their independent expected outcomes.\n", len(results))
	return nil
}

// fieldRejected selects diagnostics from the actual generated ValidateAll result.
// It does not implement a validation rule or treat unrelated fields as valid.
func fieldRejected(validationError error, field string) (bool, error) {
	if validationError == nil {
		return false, nil
	}
	problems, ok := validationError.(interface{ AllErrors() []error })
	if !ok {
		return false, fmt.Errorf("cannot inspect generated validation diagnostics: %T", validationError)
	}
	for _, problem := range problems.AllErrors() {
		diagnostic, ok := problem.(interface{ Field() string })
		if !ok {
			return false, fmt.Errorf("missing generated field diagnostic: %T", problem)
		}
		root := strings.FieldsFunc(diagnostic.Field(), func(value rune) bool { return value == '[' || value == '.' })
		if len(root) > 0 && strings.EqualFold(strings.ReplaceAll(root[0], "_", ""), strings.ReplaceAll(field, "_", "")) {
			return true, nil
		}
	}
	return false, nil
}
