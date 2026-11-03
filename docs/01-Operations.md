# Operations

## Configuration and deployment

`payload/Mods/custom-elements.json` enables the feature, uses the compiler URL
`http://custom-elements:8097` and permits batches up to 1000. The installer preserves an existing config;
inspect it on upgrades. The source example is intentionally disabled.

`CUSTOM_ELEMENTS_NETWORK` selects the existing Docker network shared by Orleans. The compiler uses
network alias `custom-elements`, internal port 8097, project `du-custom-elements`, image
`mydu-custom-elements:2026.10.17` and persistent volume `du-custom-elements_shapes`.
No host port is published. The API has no authentication and must remain on a trusted private network.
This Compose project/alias is intended for one game server. Separate worlds require separate projects,
networks/aliases, volumes and compiler URLs; do not accidentally share a registry between them.

The installer requires existing directories and verifies that Orleans belongs to the selected network.
It does not create or configure the underlying myDU server, alter game databases, or restart other services.
The package carries both compiled DLLs, all four JavaScript files and the native child resource catalog.
Only the compiler service needs the honeycomb material catalog and native icon catalog.

For manual Linux deployment, build the same Compose service on the game's network, copy the contents
of `payload/Mods` into the Orleans-mounted Mods directory, and restart Orleans. C# build scripts in
this kit target PowerShell on Windows; runtime DLLs run in the Linux Orleans container.
The Mods directory and compiler volume must be writable by their container users. The compiler runs as UID 10001.

## First-player smoke test

1. Sign in and create a small owned XS/S source with two honeycomb materials, a slope, a switch and light.
   Link the switch to the light. Keep the source core; it is excluded from exports and costs.
2. Supply enough matching honeycomb and one ordinary switch/light per requested copy in the inventory
   or linked container used by normal game inventory rules. Leave backpack output capacity available.
3. In build mode, right-click the core and choose **Custom elements → Create 1 custom item**.
   A new design should request a reconnect. Reconnect once and check delivery and exact input charges.
   Do not resubmit a pending batch just because delivery awaits reconnect.
4. Select the item. Check its name, icon, highest tier, Design ID and per-item Elements/Materials sections.
   Search for `custom` and `custom items`. The creator should see the preview pencil.
5. Place it on another construct. Check slopes/materials, collision near edges, functioning light/switch,
   and the ability to place another element across the wrapper's visible shape.
6. Pick it up unchanged and confirm stacking. Change an embedded setting, pick it up, and confirm that
   state survives redeployment. Modified/damaged instances may appropriately remain separate stacks.
7. Make a blueprint of a construct carrying the item. Ingredients should show the custom item, without
   charging its internal elements again. Deploy it and check the internal elements and links.
8. Create another copy with the amount dialog. Check cursor, OK/Cancel, feedback and input/output counts.

Native construct information currently counts embedded elements because they are real construct elements.
Mixed-item thumbnails currently show voxels without rendering embedded native models.

## Daily operation and updates

Keep the compiler healthy before Orleans/client login. Login enumerates and streams the catalog.
To inspect it, set the network variable and run these commands from the package directory:

```powershell
$env:CUSTOM_ELEMENTS_NETWORK = Read-Host 'Docker network shared with your Orleans container'
$orleans = Read-Host 'Your Orleans container name or ID'
docker compose -f ./compose.yaml ps
docker compose -f ./compose.yaml logs --tail 100 custom-elements
docker logs --tail 200 $orleans
```

Look for `CustomElements action failed`, module deployment errors, compiler HTTP failures and
`preview installation unconfirmed`. A successful preview installation is acknowledged by the client.
A successful Router HTTP response alone is not proof that an action succeeded: action exceptions are
caught and reported in logs and native player notifications.

For an upgrade, back up first, install the new package against the same compiler volume, and restart
Orleans. Fully reconnect clients after DLL/definition or resource changes. Existing definitions are
checked and refreshed without changing their item type IDs. Shared name edits appear on other copies
after those clients reconnect. Existing placed HP overrides are separate native state; this update
changes type HP and deliberately does not heal or erase damage on placed items.

## Backup and migration

Take a coordinated backup during a game maintenance window with Orleans and the compiler stopped.
Back up the entire normal game persistence set using the server's own procedure, the compiler volume,
and these Mods paths:

- `custom-elements.json` and the installed program files/version.
- `custom-element-designs.json` for shared names and creators.
- `custom-elements-jobs/` for pending/completed requests and their saved bills.
- `custom-element-groups/` for parent/child IDs, state, poses and links.

The compiler volume contains `<full-hash>.json` shape records and generated assets. Preserve it as one
unit with the databases and Mods state. Never clear the volume to fix an icon or copy only the DLLs
when migrating an existing world. Game inventories and placed element/blueprint rows refer to these IDs.
The fresh installation kit intentionally contains none of that live state.

The installer creates `Mods/custom-elements-backups/<timestamp>/` for replaced program files. This is
only a program-file rollback aid, not a world backup. A complete rollback restores a mutually compatible
set of Mods files, compiler image, compiler volume and game/feature state. Removing the feature from a
world still containing custom items leaves unresolved definitions and resources.
Do not use `docker compose down -v` on a world whose custom items you want to keep.

## Troubleshooting

| Symptom | Checks |
|---|---|
| No custom menus | Confirm both DLLs loaded, config enabled, correct Mods mount, restart Orleans and reconnect. |
| Login stalls or catalog fails | Check compiler health/network/DNS and duplicate SyncLoader mods. |
| Missing mesh | Check compiler registry/assets survived, catalog was streamed and client version matches. |
| Missing icon with visible mesh | Reopen inventory; reconnect; inspect resource ordering and flag-6 refresh logs. |
| No rename button or Design ID | Confirm preview script, startup retries and client ACK; creator permission affects the pencil. |
| Native icons/names corrupted | Verify isolated child aliases and sibling definitions; do not derive aliases from native leaf nodes. |
| New type not delivered | Reconnect, inspect saved job lastError, input availability and backpack capacity. |
| Changed source returns old shape | Wait for native voxel meshing and retry; stale/missing source meshes are rejected. |
| Missing internals after deployment | Inspect group status and deployment errors; preserve failed state before retrying pickup. |
| Repair Unit shows Error Item | Known unsupported internal replacement/ingredient path; see Limits. |

Do not delete saved jobs or group files as an exploratory fix. Record the action error and preserve state
before attempting recovery. Administrative fixtures are development tests, not normal installation steps.

