# Clean package consumers

These executable probes verify candidate package composition after the source solution has been built and packed. They are invoked by the repository CLI's `repository verify` command, locally and in the Linux/macOS/Windows CI matrix.

- `Core/` references only `Knightfall.Envoy.ControlPlane` plus the development analyzer. It loads Core, constructs a snapshot, checks consistency and Delta versions, exercises a transitive Protocol message, and checks the exact resolved package graph and absence of an ASP.NET shared-framework requirement.
- `AspNetCore/` references only `Knightfall.Envoy.ControlPlane.AspNetCore` plus the analyzer. It loads the package, uses transitive Protocol, verifies the hosting graph/shared-framework requirement, and builds/disposes an application host without starting a server.
- `ConsumerChecks.cs` contains the shared package/serialization/framework checks.

The `.csproj.template` files are copied into separate temporary projects after packing. Each project receives the repository's pinned SDK, central versions, shared build policy and analyzer configuration, and restores twice (including locked restore) into a fresh NuGet cache. The temporary feed configuration includes local candidate packages and NuGet.org dependencies. Source `ProjectReference` items are forbidden. Resolved locks and runtime configuration evidence are saved under Protocol's `obj/upstream/results/root-consumers/`; temporary projects are removed afterward.

These probes establish package-consumption and dependency-boundary evidence. The complete Core test suite also runs in a separate package-only consumer. The fresh consumers use the new Knightfall package IDs while loading the retained Envoy.ControlPlane assembly names. The complete Core and Hosting suites cover the current root implementation again against these packages; optional evaluator conformance remains separate T26/GV work.
