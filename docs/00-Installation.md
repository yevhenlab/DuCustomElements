# Install Custom Elements on your myDU server

Use a working myDU Docker server and the unmodified **1.4.18-mydu** client. Back up your world before installing. The package supplies server mods and a private compiler service; players install no client files.

## 1. Find your server paths

Run these commands in your myDU server directory, which contains its Docker Compose file:

```powershell
docker compose ps orleans
$orleans = docker compose ps -q orleans
if (-not $orleans) { throw 'Start your myDU server before continuing.' }
docker inspect $orleans --format '{{range .Mounts}}{{println .Source "->" .Destination}}{{end}}'
docker inspect $orleans --format '{{json .NetworkSettings.Networks}}'
```

Find the host directory mounted at `/OrleansGrains/Mods`. Use that host path as `-ModsDirectory`. Choose a network listed for Orleans as `-GameNetwork`. Use the container ID in `$orleans` as `-OrleansContainer`.

Host paths vary by installation. Use the mount source reported by Docker; `/OrleansGrains/Mods` is the path inside the standard server container, not a required host location.

Copy the running server assemblies to a new local directory for the compatibility check:

```powershell
$assemblies = Join-Path $env:TEMP ('mydu-assemblies-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $assemblies | Out-Null
docker cp "${orleans}:/OrleansGrains/." $assemblies
if ($LASTEXITCODE -ne 0) { throw 'Could not copy server assemblies.' }
```

This copies the effective container files, including mounted assembly overrides. Keep that directory local; it can also contain your installed Mods and must not be redistributed. No server password or backoffice login is required by the mod installer.

## 2. Install the package

Open PowerShell in the extracted Custom Elements package directory. Set your actual values:

```powershell
$mods = Read-Host 'Host Mods directory reported by docker inspect'
$network = Read-Host 'Docker network shared with your Orleans container'
./scripts/Verify-Package.ps1
./scripts/Install.ps1 -ModsDirectory $mods -ServerAssemblies $assemblies `
  -GameNetwork $network -OrleansContainer $orleans -PlanOnly
```

Review the printed targets, then apply:

```powershell
./scripts/Install.ps1 -ModsDirectory $mods -ServerAssemblies $assemblies `
  -GameNetwork $network -OrleansContainer $orleans
```

The installer builds/starts the compiler, waits for health, stops Orleans, backs up replaced program files, copies the payload and restarts Orleans. Existing `custom-elements.json` is preserved. Review it on upgrades, especially `enabled` and `compilerUrl`.

The DLLs are checked against `compatibility.json`. If the check fails, review [Development](02-Development.md) before using `-Rebuild`; another server build requires testing. A second mod named `SyncLoader` must be integrated before installing this loader.

The package directory must remain available for Compose operations. Do not expose compiler port 8097 to the internet. The compiler uses the game Docker network and needs no additional public port, domain change or client executable patch.

## 3. Know which files go where

| Package file | Destination or use |
|---|---|
| `payload/Mods/ModCustomElements.dll` | Your server's mounted Mods directory; main feature. |
| `payload/Mods/ModCustomElementsLoader.dll` | Same directory; synchronous login resource loader. |
| `payload/Mods/custom-elements.json` | Same directory; settings. |
| `payload/Mods/custom-elements-panel.js` | Same directory; creation amount dialog. |
| `payload/Mods/custom-elements-name-panel.js` | Same directory; shared name editor. |
| `payload/Mods/custom-elements-preview.js` | Same directory; inventory details and rename control. |
| `payload/Mods/custom-elements-ui-probe.js` | Same directory; administrative UI diagnostic. |
| `payload/Mods/element-resources.json` | Same directory; native component resource metadata. |
| `compose.yaml` and `source/` | Keep in the package directory; Compose builds the compiler from this source. |
| `compatibility.json`, `manifest.json`, `scripts/` | Package verification, installation and optional rebuilding. |

Copy all eight payload files together. JavaScript is sent to clients by the server. Do not put it in the client installation or copy `source/` into Mods.

The compiler stores generated designs/assets in Docker volume `du-custom-elements_shapes`. The mod creates `custom-element-designs.json`, `custom-elements-jobs/` and `custom-element-groups/` beside its DLL. Preserve these together with normal world persistence; see [backup and migration](01-Operations.md#backup-and-migration).

## 4. Use it in game

1. Reconnect after installation. Build an owned XS/S construct, or use an organization source as a legate. Include honeycomb, ordinary elements, or both.
2. Supply matching honeycomb and one matching ordinary item per embedded element per copy. Leave backpack capacity for output. The source core is excluded and the source stays intact.
3. In build mode, right-click the source core and choose **Custom elements → Create custom items** for a batch of 1–1000, or use the fixed 1/100 actions.
4. Reconnect when a new design requests it. The saved batch is charged and delivered then; do not submit it again while waiting.
5. Place the custom item. Embedded elements function normally and internal links are restored. Unchanged pickup stacks; modified settings, contents or damage can retain a separate instance.
6. Inspect the item for its Design ID, materials and elements. Its creator can rename the shared design with the pencil or placed-item rename action. Other clients reload shared names on reconnect.

Blueprint deployment charges the custom wrapper item and restores its internals. Repair Tool inventory replacement and Maintenance Unit reconstruction remain incomplete. Read [limits](03-Limits.md) and run the [first-player smoke test](01-Operations.md#first-player-smoke-test) before opening the feature to players.

