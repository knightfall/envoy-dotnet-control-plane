# Envoy.ControlPlane foundation

This package establishes the Core dependency boundary and build/pack layout. Resource entries and snapshots begin in T05; caches, watches and protocol engines follow in later tasks. This foundation does not yet implement a control-plane runtime.

Core references the authoritative Protocol bindings. It has no ASP.NET hosting or optional validation/CEL dependency. Consumers choose their own hosting and transport. Validation remains explicitly invoked through the separate optional package.
