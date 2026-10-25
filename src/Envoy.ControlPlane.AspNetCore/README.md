# Envoy.ControlPlane.AspNetCore foundation

This package establishes the hosting dependency boundary and build/pack layout. Generated service adapters, dependency injection and endpoint mapping remain later implementation tasks. This foundation does not register or serve xDS endpoints.

The package references Core and the ASP.NET Core shared framework. Protocol types arrive through Core; the optional validation evaluator is not a dependency. Applications retain ownership of host configuration, authentication, transport limits and shutdown.
