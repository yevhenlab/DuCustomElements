# Source map

All paths below are relative to `source/`. Source is included so the feature can be reviewed and rebuilt
without this development checkout. The original investigation diary, game binaries and live exports are
intentionally absent.

## Server C#

| File | Responsibility |
|---|---|
| MyDuMod.cs | Service setup, menu actions, source validation, creation jobs, input/output transaction and player feedback. |
| Loader/MyDuMod.cs | Separate SyncLoader mod, catalog streaming and native login release. |
| Compiler.cs | HTTP bridge to Python, gameplay material names and reserved type IDs. |
| Definitions.cs | Wrapper/private child YAML, tier/mass/volume/voxel-only HP, strict validation and import. |
| BankNotifications.cs | Preserve and serialize native gameplay-bank change callbacks. |
| Assets.cs | Per-player asset phase ordering, alias loading and native inventory icon refresh. |
| ChildResources.cs | Native resource subtree copies under isolated component aliases. |
| Bounds.cs | Validate/register wrapper bounds using native cache reflection and snapshot publication. |
| CollisionRules.cs | Filter wrapper placement interference while retaining player mesh collision. |
| SourceElements.cs | Export non-core native elements, saved blueprint properties and local-key links. |
| Costs.cs | Exact per-copy honeycomb fixed-point quantities and ordinary embedded item costs. |
| Jobs.cs | Persistent request storage and shared loader runtime callbacks. |
| Models.cs | Settings, Shape/assets/component data, dialog and job contracts. |
| Designs.cs | Creator/shared names, validation, default construct-derived naming and migration seed. |
| Rename.cs | Preview catalog/included contents, creator permissions and shared-name dialog submission. |
| PreviewDelivery.cs | Bounded startup retries, nonce ACK and reconnect isolation. |
| ModuleHooks.cs | Install native hooks, template cache, grouped deployment/movement/link handling. |
| ModuleGroups.cs | Persistent group/child models and copied in-memory/disk state. |
| ModuleTransforms.cs | Local/world transforms and movement via a selected child. |
| ModulePickup.cs | Group detach/pickup/delete, contents checks, instance preservation and cleanup. |
| ModuleInventory.cs | Compare meaningful saved settings/links against template for pristine stacking. |
| ModuleBlueprints.cs | Blueprint ingredient filtering, paid/raw deployment expansion and targeted recovery. |
| BlueprintModules.cs | Blueprint instance matching and link ID remapping. |
| LiveModuleCheck.cs | Admin native lifecycle fixture and cleanup assertions. |
| LiveBlueprintCheck.cs | Admin paid blueprint fixture, charge checks and cleanup. |
| build.ps1 | Compile two .NET 6-targeted DLLs against the supplied server implementation assemblies. |
| Tests/Checks.csproj, Tests/Program.cs | Native gameplay-bank, HP, costs, bounds, aliases, links, pickup, names, job and preview checks. |

## Compiler Python

| File | Responsibility |
|---|---|
| service.py | Internal HTTP API, limits, error responses and startup arguments. |
| registry.py | Immutable registry, type allocation, material lookup, asset packaging and older asset upgrades. |
| cell_codec.py | DU compressed cell codec and sample representation. |
| shape.py | LOD0 sample validation, normalization, canonical hash and occupied material volume. |
| modules.py | Incorporate native component transforms/configuration/links into canonical identity. |
| geometry.py | Read native GLB, assign native materials, generate visual/collision/node/nqdef resources. |
| native_mesh.py | Unigine ms11 binary encoding. |
| thumbnail.py | Software voxel mesh thumbnail rendering. |
| test_shapes.py | Identity, quantities, resources, registry and component tests. |
| test_native_mesh.py | Independent ms11 reader and generated material-surface checks. |
| requirements.txt | Pinned Python libraries. |
| Dockerfile, .dockerignore | Minimal compiler runtime, non-root user and build input allowlist. |
| materialdefs.nqdef | Matching vanilla honeycomb material resource definitions. |
| element-icons.json | Native icon metadata used for element-only item previews. |
| element-resources.json | Native resource subtrees consumed by the server mod, not Python runtime. |

## Client UI and deployment

| File | Responsibility |
|---|---|
| custom-elements-panel.js | Amount MousePage, enhanced mouse, input validation, submit/cancel and feedback. |
| custom-elements-name-panel.js | Native DU MessageBox shared-name editing. |
| custom-elements-preview.js | Native inspector pencil, Design ID, per-item contents, native style and selection tracking. |
| custom-elements-ui-probe.js | Read-only admin inspector diagnostics. |
| test_panel.cjs, test_preview.cjs | Node-based amount/rename and inspector behavior tests. |
| custom-elements.example.json | Disabled source settings example. |

Package-root `compose.yaml` parameterizes the external game network and adds a compiler health check.
`payload/Mods/` is the ready-to-copy runtime payload, with enabled default config.
`scripts/Install.ps1` validates compatibility and performs a backed-up program deployment.
`scripts/Verify-Package.ps1` checks file hashes; `scripts/Test-Source.ps1` builds/runs C#/JS checks.
`compatibility.json` records the tested client and server assembly fingerprints.
`manifest.json` inventories the release files and SHA-256 hashes.


