# T04 build and package foundation

Implemented 25 October 2026 after the T03 source decision in PR #35 and project-based tooling migration in PR #37. This is build/package evidence; Core resource/cache behavior and ASP.NET xDS endpoints remain later tasks. No NuGet package has been published by this task.

## Structure and boundaries

[Envoy.ControlPlane.slnx](../../Envoy.ControlPlane.slnx) contains all ten maintained projects: five package projects, two tooling projects and the three existing xUnit/native-MTP suites. The repository command compares the solution with maintained project files before building. Historical provider research under `tests/validation/` stays outside the active solution.

- `Envoy.ControlPlane` references Protocol. Its candidate version is `0.1.0-t04`; resource entries and snapshots begin in T05.
- `Envoy.ControlPlane.AspNetCore` references Core and the ASP.NET Core shared framework. Its candidate version is `0.1.0-t04`; service adapters and endpoint registration remain pending.
- Protocol, Annotations and the optional source-built Validation candidate retain version `0.1.0-t03`, their pins, generated fingerprints and ownership arrangement.
- Core and hosting have no optional evaluator/CEL dependency. The annotations-only package remains a transitive Protocol dependency.

Root `Directory.Build.props` owns nullable checking, warnings as errors, dependency locks and deterministic compilation. `src/Directory.Build.props` owns shared package metadata, notices, portable symbol packages and source information. `tools/Directory.Build.props` imports the root policy. Versions remain centralized in `Directory.Packages.props`; existing analyzer references and xUnit/MTP pins are preserved.

All five candidate packages include their assembly, readme, repository license and a separate portable-symbol archive. Protocol, Annotations and the authored package projects include XML documentation. The fingerprinted third-party evaluator retains its existing missing-XML-documentation limitation; no compiler warning suppression or source adaptation was added for it. Original third-party notices remain packaged by their existing projects.

## Verification

Use SDK **10.0.400**, Go **1.26.4**, Buf **1.75.0**, the checked-in dependency locks and an external source cache:

```sh
dotnet restore tools/Envoy.ControlPlane.Tools/Envoy.ControlPlane.Tools.csproj --locked-mode
dotnet run --project tools/Envoy.ControlPlane.Tools -- bindings generate --cache <external-source-cache>
dotnet run --project tools/Envoy.ControlPlane.Tools -- repository verify
```

The repository command performs locked solution restore, Release build, native solution tests, Go comparison, solution pack, package-content/symbol inspection, the full Protocol/Validation NuGet-consumer suites, and executable Core/ASP.NET consumers. Each package consumer uses a separate empty NuGet cache and no source project reference. Consumer locks, runtime configuration and test reports are saved beneath Protocol's `obj/upstream/results/`; packages remain under its `bin/packages/`.

Observed locally on macOS arm64:

| Check | Result |
|---|---|
| Solution registration | All ten maintained projects included |
| Release solution build with Roslynator | 0 warnings, 0 errors |
| `dotnet test --solution Envoy.ControlPlane.slnx --configuration Release --no-build --no-restore` | Passed: 54, Failed: 0, Skipped: 0 |
| Independent Go PGV comparison | 33/33 cases matched |
| Fresh Protocol package consumer | Passed: 12, Failed: 0, Skipped: 0 |
| Fresh Validation package consumer | Passed: 18, Failed: 0, Skipped: 0 |
| Core executable consumer | Loaded Core, serialized a transitive Protocol message, verified the exact package graph and no ASP.NET framework requirement |
| ASP.NET executable consumer | Loaded hosting, verified its exact package graph/framework requirement, built and disposed an application host |
| Package inspection | Five `.nupkg` and five `.snupkg` archives; required assemblies, notices, readmes, documentation where supplied, and PDBs present |

The consumers do not start network listeners. Their templates and shared checks live under [tests/consumers](../../tests/consumers/README.md). Existing unit assertions and suite sizes are unchanged. CI runs generation and the repository command on Linux, macOS and Windows; remote results must be checked for the implementing PR before merge.

This establishes the initial T04/V37 package graph and consumption smoke proof. It does not establish control-plane runtime behavior, full validator conformance, live Envoy interoperability, or release publication readiness.
