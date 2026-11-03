# Compatibility and acceptance checks

The bundled runtime targets **myDU client 1.4.18-mydu** and the server assembly fingerprints in `compatibility.json`. Keep the matching client resource catalogs bundled with this release.

Recorded checks for runtime version 2026.10.17 include C# compilation with warnings treated as errors, native definition/cost/grouping/blueprint checks, 16 JavaScript tests, and a compiler image build. The Python suite recorded 18 passes and two optional fixture skips. These records apply to that runtime build, not to every myDU installation.

A second pristine server has not been tested end to end. Before enabling the feature for players, complete the [installation guide](00-Installation.md) and [smoke test](01-Operations.md#first-player-smoke-test) on your server. Re-run them after server/client updates.

Check material charges, reconnect delivery, icons, collision, functioning embedded elements and links, pickup/stacking, modified state retention, and blueprint deployment. Test with a disposable construct and retain normal world backups.

Inventory-based Repair Tool replacement and Maintenance Unit reconstruction are incomplete; see [limits](03-Limits.md). Successful creation or blueprint deployment does not validate these repair paths.

For reproducible source checks, use [Development](02-Development.md#build-and-tests). Administrative lifecycle fixtures change temporary world objects and are optional development tools; installation never runs them automatically.
