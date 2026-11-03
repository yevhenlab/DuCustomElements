# Current limits and PvP/repair behavior

## Damage model

The custom shape is an element mesh, not newly placed honeycomb. It does not create removable voxel holes
or contribute its shape as native honeycomb/CCS. Its visual/collision mesh follows the source geometry.
Native elements inside it are real, separately damaged elements. Destroying wrapper HP does not explicitly
propagate damage to all its children or guarantee that they stop operating.

Wrapper base HP is `sum(occupied material m3 * native honeycomb HP per m3)`.
One full 0.25 m voxel contributes 1/64 of a cubic metre's HP; slopes use their occupied fraction.
Embedded element HP is excluded. An element-only wrapper retains the minimum fallback of 1 HP;
its children retain native HP. This avoids giving the package their HP twice.
Placed talents and existing dynamic HP properties are still governed by native game behavior.

Wrapper resistances remain inherited from the furniture definition, rather than blending honeycomb
resistances. Server damage area calculations may use bounds rather than the exact triangle mesh.
Precise projectile/area-damage parity with a real honeycomb construct has not been established.

## Repair status: deliberately deferred

Observed in the live game: damaged wrapper mesh and internal elements can be repaired separately with
scrap. User PvP tests confirmed that both can be destroyed and require individual repair actions.
The code does not currently make one repair action restore the whole package.

Known unfinished paths:

- Manual Repair Tool replacement with an inventory item can throw an error.
- Maintenance/Repair Unit reconstruction can show private internal types as Error Item ingredients.
- A repair unit can count the wrapper and its internal components independently, risking duplicate costs.
- Blueprint deployment hooks do not automatically fix repair-unit code paths that inspect raw snapshots.

These are not solved by the blueprint ingredient fix. The installed feature must not be described as
supporting complete inventory-based replacement or automated reconstruction. No repair implementation
changes are included in this installation/documentation task.

Future work should define one consistent charging rule, map private children back to public ingredients,
restore module groups after unit repair, and preserve damage/restoration counts and native restrictions.
Repair tests must cover wrapper-only damage, child-only damage, both, permanent destruction and fuel/state.

## Other limits

- Sources currently support XS/S (geometry size 32/64), up to 128 non-core DU elements.
- Nested custom modules and territory units are rejected.
- Geometry export rejects unsupported/stale/missing data; mesh triangle limit is 100,000.
- Every connected client currently receives the entire saved catalog. There is no demand loading,
  automatic garbage collection, catalog quota or proven large-catalog performance guarantee.
- Mixed voxel/native thumbnails show the voxel mesh; embedded native models are listed but not composited.
  Element-only items use a native element icon.
- New definitions and shared name changes require client reconnects. Startup retries repair UI injection
  timing but do not remove the native item-bank reload requirement.
- Children are not isolated from the destination construct. External links, brakes, lights and fuel systems
  behave as native elements, and construct statistics can list/count those children.
- Grouping is a wrapper plus native children, not a separate construct or a fully atomic database object.
  Interrupted multi-step operations retain status files for investigation; arbitrary crash recovery is
  not claimed to be complete. Retain failed group/job state instead of deleting it.
- Unchanged pickup stacks; damage, changed settings, scripts, links or contents can produce an instance.
  Do not remove dynamic properties if you intend to preserve those changes.

