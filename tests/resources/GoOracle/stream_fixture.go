// Copyright 2018 Envoyproxy Authors
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.

// Modified from mockStream in pkg/server/v3/server_test.go at
// 67c5345367582741334e34ffc66a561b0ce32946. See ../UPSTREAM-TESTS.md.
// Replaces testing.T assertions with recorded observations, and adds cooperative
// cancellation and explicit receive errors for cross-language trace handoffs.
package main

import (
	"context"
	"io"

	discovery "github.com/envoyproxy/go-control-plane/envoy/service/discovery/v3"
	"google.golang.org/grpc"
)

type streamInput struct {
	request *discovery.DiscoveryRequest
	err     error
}

type mockStream struct {
	grpc.ServerStream
	ctx     context.Context
	recv    chan streamInput
	sent    chan *discovery.DiscoveryResponse
	observe func(*discovery.DiscoveryResponse)
}

func makeMockStream(ctx context.Context, observe func(*discovery.DiscoveryResponse)) *mockStream {
	return &mockStream{
		ctx:     ctx,
		recv:    make(chan streamInput),
		sent:    make(chan *discovery.DiscoveryResponse, 1),
		observe: observe,
	}
}

func (stream *mockStream) Context() context.Context {
	return stream.ctx
}

func (stream *mockStream) Send(response *discovery.DiscoveryResponse) error {
	stream.observe(response)
	select {
	case stream.sent <- response:
		return nil
	case <-stream.ctx.Done():
		return stream.ctx.Err()
	}
}

func (stream *mockStream) Recv() (*discovery.DiscoveryRequest, error) {
	select {
	case item, more := <-stream.recv:
		if !more {
			return nil, io.EOF
		}
		return item.request, item.err
	case <-stream.ctx.Done():
		return nil, stream.ctx.Err()
	}
}
