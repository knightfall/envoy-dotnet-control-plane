package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"

	_ "github.com/envoyproxy/go-control-plane/envoy/config/core/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/extensions/access_loggers/file/v3"
	_ "github.com/envoyproxy/go-control-plane/envoy/type/v3"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/reflect/protoreflect"
	"google.golang.org/protobuf/reflect/protoregistry"
)

type validationCase struct {
	ID            string `json:"Id"`
	Type          string `json:"Type"`
	Data          string `json:"Data"`
	ExpectedValid bool   `json:"ExpectedValid"`
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
		if validationError != nil {
			actual.Error = validationError.Error()
		}
		results = append(results, actual)
		if actual.Valid != item.ExpectedValid {
			return fmt.Errorf("%s: expected valid=%t, got valid=%t: %s", item.ID, item.ExpectedValid, actual.Valid, actual.Error)
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
