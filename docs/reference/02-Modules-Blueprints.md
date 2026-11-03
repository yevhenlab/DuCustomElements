# Embedded native elements, pickup and blueprints

## Native grouping model

A placed custom item is one furniture wrapper plus ordinary native element instances, tracked as a group.
It is not a new construct. Lights, switches, brakes, tanks and other children use native game logic,
state, fuel behavior and links. Other destination elements can link to those children.
The native construct information window may include their counts. The source core is not copied.

Private child types start with `CustomPart<hash>_<original-type-id>`. They are cloned as siblings of the
original leaf definition, retain the complete native YAML (including client-only metadata), and receive
an isolated asset alias that references the original node/icon. Deriving a child under a native leaf can
turn that original into a category and break its name/icon. Reusing a native asset alias can overwrite
its client resource entry. Both earlier problems are avoided by the current implementation.

Child definitions use `hidden: false`, `displayHidden: true`, `isTradable: false`, and zero static mass.
Native blueprint validation rejects fully hidden types. These components are implementation details,
not separately purchasable ingredients. Their own HP and runtime state remain native.

## Hook points

`ModuleHooks.Install` replaces ElementManagementGrain ElementAdd, MoveElement, ElementDelete and
ElementDestroy; it checks SalvageElement before the native call. BlueprintGrain GetBlueprintInfo,
Use and UseRaw are also intercepted. Non-custom operations normally invoke the original call.
A gate serializes group lifecycle changes within the mod; this is not a distributed database transaction
covering all files and native grain calls.

On parent deployment, native parent placement occurs first. The template is looked up and cached without
large mesh buffers. Each child is transformed from template-local coordinates into parent position and
rotation, assigned its private type, and added through native construct-element APIs. Saved instance IDs
are reused for modified packages. Links are restored after children exist.
Group state is saved during deployment; an interruption records `failed` and reports recovery guidance.
Moving through a selected child resolves the parent and applies the inverse local transform, so the whole
package moves/rotates together. Picking up or deleting a grouped child resolves the parent package too.
Independent salvage is guarded so internal parts cannot be extracted as a second ingredient reward.

## Pickup, state and stacking

Pickup snapshots child properties and links, applies native ownership/build/pickup restrictions, detaches
children, and stores one wrapper item. Native inventory property filtering removes placement-only values
from the wrapper. Earlier unconditional hitpoints/elementsColliding properties made pristine items fail
to stack; that was corrected.

A package returns to a zero-instance stack only if it matches the immutable template after normalization:
unchanged saved settings/scripts/names, links and relevant state, no retained contents, no wrapper damage
or restoration state. Talent/recomputed static fields are excluded from the comparison. Unknown meaningful
state is preserved conservatively. Private database child rows/group metadata no longer needed by a
pristine package are removed. A modified package retains IDs and saved state for redeployment.

Damaged or modified items can therefore legitimately show dynamic properties and remain separate.
The client Design ID display itself is not stored as dynamic inventory state. Removing dynamic properties
is a destructive user reset of saved instance state, not the automatic stacking strategy.

## Blueprint path

Blueprint snapshots continue to include component rows so native saved-property/DRM/talent filtering can
operate on actual native elements. User-facing blueprint ingredients remove private children, and paid
deployment excludes those types from native ingredient consumption. The wrapper was already paid for
with all original component inputs at creation, so the blueprint charges one custom item per package.

After native blueprint deployment, the mod expands each wrapper from snapshot component rows, restores
saved settings/links and records its group. Raw deployment can adopt already copied child instances
instead of duplicating them. Matching uses type and transform, including equivalent quaternion signs.
Internal and external link IDs are remapped. Older wrapper-only blueprints can fall back to the saved
immutable template, with the limitation that absent snapshot state cannot be reconstructed from nothing.

The paid blueprint fixture verifies ordinary ingredient charges and no duplicate private-child charge.
The lifecycle fixture verifies placement, movement, pickup, pristine stacking, modified ID/state retention,
links and cleanup. They temporarily modify the game world and require admin context; they are not run by
Install.ps1. Normal installation validation uses an explicitly created test construct.

This blueprint path does not intercept every native repair/reconstruction path. Maintenance Unit repair
reads snapshots differently and is intentionally documented as unfinished.
