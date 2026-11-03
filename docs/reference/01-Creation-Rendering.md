# Creation, identity and rendering

## Execution path

`MyDuMod.GetModInfoFor` publishes element/construct creation actions and starts preview delivery.
`ValidateSource` accepts player-owned sources or organization sources requested by a legate, with native
geometry size 32/64 and dynamic/static/space kind. Core selection is additionally validated for element menus.
`SourceElements.Read` excludes core units and exports at most 128 remaining native elements plus links.
Only blueprint-saved dynamic properties enter the immutable template. Nested custom definitions and
territory units are rejected. Links use local component keys; key 0 denotes the source core endpoint.

`Generate` calls the native voxel service for current cells and meshes, then posts to `/prepare`.
Python validates samples and source mesh freshness, canonicalizes the content and creates/reuses a saved
design. The C# side saves the creator/shared name, imports gameplay definitions, registers bounds,
remembers the module template, streams resources and saves a billed creation job.

For a definition absent from the player's login item-bank snapshot, delivery waits for reconnect.
The synchronous loader then supplies assets and completes eligible pending jobs. Existing known types
can be delivered immediately. Source geometry and installed source elements remain unchanged.

## Identity and data contract

`Shape` carries hash, native definition name, itemTypeId, materialVolumes, assets, bounds,
sourceMeshCenter, embeddedElements and embeddedLinks. C# deserializes registry results into this model.
Each embedded record has key, original public itemTypeId, assetAlias, position, rotation and properties.
Links reference keys until translated into actual deployed element IDs.

Canonical SHA-256 includes relevant LOD0 voxel coordinates/material weights/deformation and native
component configuration/relative transforms/links. Translation and chunk order do not create new designs.
Renaming is separate metadata and does not change hash, item type, assets or stack identity.
The registry checks existing content at a hash before reuse and resolves 32-bit native item-name hash
collisions against occupied gameplay IDs. Full hashes remain available as Design ID in the item preview.

The compiler stores one immutable record per design. Multiple copies refer to one type and shared assets;
the game still stores each placed wrapper/child instance. Ten identical items therefore do not require
ten independent mesh compilations, but their deployed native children still have individual state.
Do not equate saved JSON/asset sizes with measured process RAM: base64, object caches, client meshes,
physics, instance state and native allocations have different overheads. No universal RAM-per-item ratio
is asserted by this kit.

## Geometry and resources

`cell_codec.py` handles native compression, cell samples and material mapping. `shape.py` derives occupied
material volumes from weighted samples, skips flagged samples and normalizes geometry. `geometry.py`
reads native LOD0 GLB triangles and maps material surfaces. `native_mesh.py` writes Unigine **ms11**
static meshes. Visual/collision assets, node, materials, nqdef and thumbnail are packaged under
`resources_generated/custom_elements/` using hash-based names.

The visible item preserves material boundaries and slopes. A separate full triangle collider provides
player collision. The server's bounding-box cache is updated using a copy of its dictionary, while
CollisionRules filters wrapper interference reports so element placement can overlap the visible mesh.
This deliberately separates player collision from placement interference. Native children continue to
use native placement rules. This is not a change to the core's building zone.

Registry requests are limited to 24,000,000 bytes, samples to 300,000 and mesh triangles to 100,000.
Missing/stale meshes are rejected instead of fabricating a stale item. Assets must remain under the
custom resource prefix, without `..`, and use only allowed delivery kinds.

## Gameplay definitions and costs

`CustomVoxelItems` is a public category under FurnituresAppliances with display name **Custom items**.
The wrapper's native name begins `CustomVoxel`, with matching assetAlias, visible/tradable flags,
maximum component tier, combined mass/volume and voxel-only HP. Import retains client-only YAML fields
while validating the server-supported subset. Bank changes run under a registration semaphore and
serialize native `_onChange` listeners to avoid concurrent ItemPermissionCache rebuild failures.

For honeycomb material volume v in cubic metres:

- Mass adds `native UnitMass * v`.
- Inventory volume adds `native UnitVolume * v`.
- Wrapper HP adds `native Hitpoints * v`.

For each embedded native element, mass and volume add its native values and tier can increase.
Its HP does not add to wrapper HP. Wrapper HP falls back to 1 if there is no honeycomb HP.
Private child definitions retain native HP/volume/tier but use zero static mass to avoid double mass
on placement. Native dynamic effects, such as container/fuel contents, still belong to actual children.

`Costs.ForCopies` converts material cubic metres through native 24-bit fixed-point quantities
(1 m3 = 16,777,216 units) and multiplies by the copy count. It adds one original public item per native
component per copy, excludes the core, uses checked arithmetic and rejects invalid volumes.
`Finish` takes those inputs through native PlayerInventory rules, including the linked primary container,
and delivers to PlayerInventoryWithoutPrimary (backpack) in one native inventory transaction.

Job status becomes `granting` before commit and `completed` afterward. A commit/ack crash can be ambiguous;
`granting` is deliberately not blindly auto-retried. This is not a claim of exactly-once execution across
arbitrary database/file-system crashes. See the persistence reference before manual intervention.

## Native client loading protocol

The separate loader mod is named **SyncLoader**. Stock DU waits for its action 0 during login.
Resources are delivered with native `VoxelCsgApplied`/`VoxelEdit`, constructId 0:

| Flag | Purpose in this implementation |
|---|---|
| 2 | Generated resource bytes, with resource path in hashContext. |
| 1 | nqdef JSON defining wrapper and isolated native child aliases. |
| 3 | Load each element-library alias by hashContext. |
| 6 | Refresh native ItemLib icon map after aliases are loaded. |
| 8 | End synchronous loading; emitted in loader finally block. |

Delivery is serialized per player. Phase delays are 1000 ms after resource bytes, 1000 ms after nqdef,
500 ms after aliases and 500 ms after icon refresh because PubSub awaits do not ensure arrival order.
ItemLib's icon map is separate from ElementLibrary: flag 3 alone leaves inventory icons missing.
The loader also preloads resources during GetModInfoFor, before the normal inventory icon map initializes.

UI scripts arrive through native ModTriggerHudEvent `modinjectjs`, rather than a patched client file.
Preview delivery retries up to 30 times, every 2 seconds, until action 22 acknowledges its session nonce.
It does not block login. ItemInspector prototype hooks cover inventory and detached popups; native item
bank lookups use string keys. The item-type marker and hash fallback track inspector reuse.

The pencil opens DU's native MessageBox; the amount panel uses MousePage with enhanced mouse enabled.
Contents use native package/drilldown components, section color, right-aligned counts and Play font.
The full Design ID is presentation metadata in the inspector, rather than a per-instance dynamic
property that would unnecessarily split every stack.
