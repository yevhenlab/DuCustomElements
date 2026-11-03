<div align="center">

# Custom Elements for myDU

**Turn your builds into reusable items.**

Create custom elements from honeycomb and working DU components, then place them on other constructs.

[Install on your server](docs/00-Installation.md) · [Player guide](#use-it-in-game) · [Known limits](docs/03-Limits.md)

**Experimental** · Tested with myDU client **1.4.18-mydu** · No client patches

</div>

---

## Build once. Place again.

Design a small construct with the shape, materials and components you want. Custom Elements turns that design into an inventory item you can make and place again.

A light and switch built into your design remain working elements when you place the item. Their internal link is restored, too.

**Build a design → Create items → Place them on your constructs**

Your original construct stays intact. Each copy consumes the matching honeycomb and ordinary DU element items.

## What you get

| Feature | In game |
|---|---|
| **Custom shapes** | Keep your source's voxel shape, slopes and materials. |
| **Working components** | Include native DU elements and their internal links. The source core is excluded. |
| **Batch creation** | Make 1–1000 copies, with materials charged per copy. |
| **Inventory details** | See an icon, Design ID, materials and included elements. Filter under **Custom items**. |
| **Shared names** | The design creator can name the item for all copies. |
| **Pickup and reuse** | Unchanged items stack; modified items can preserve their individual state. |
| **Blueprint support** | Deploy custom items with their internals, without paying for those components twice. |

## Install on your server

The feature adds **two mod DLLs and a separate Docker compiler service**. It does not rebuild or replace the official server executables. The mods extend server behavior while loaded and save custom-item data with your world.

Players use the standard client. Resources and UI controls are delivered by the server.

### You need

- A working myDU Docker server and the **1.4.18-mydu** client.
- PowerShell and Docker Compose v2 with `up --wait` support.
- Your server's Mods directory, Orleans container and Docker network.
- A local copy of your server's effective assemblies for the compatibility check; the guide explains how to obtain it.

### Get started

1. **Back up your world.**
2. **Follow the [installation guide](docs/00-Installation.md).** Identify your own server paths, preview the installation, then apply it.
3. **Reconnect and run the [smoke test](docs/01-Operations.md#first-player-smoke-test).** Check creation, placement, pickup and blueprints before enabling the feature for players.

The installer verifies compatibility, starts the compiler and briefly stops Orleans to install the mod files. It backs up replaced program files and preserves an existing feature configuration.

> **Already using SyncLoader?** This package supplies a mod with that name. Integrate it with your existing synchronous loader before installation.

## Use it in game

1. Build an **XS or S construct** that you own, or use an organization construct as a legate.
2. Supply the matching honeycomb and component items, and leave backpack space for the output.
3. In build mode, right-click the source core and choose **Custom elements → Create custom items**. Enter the number of copies.
4. If the design is new, **reconnect once** when prompted. Your saved batch is charged and delivered then; do not submit it again while waiting.
5. Place the item on another construct and use its components normally.

The creator can rename a design through the inventory preview pencil or the placed item's rename action. Other clients see the shared name after reconnecting.

## Know the limits

> **Repair support is incomplete.** Repair Tool replacement from inventory and Maintenance Unit reconstruction have known problems. Review the [full limits](docs/03-Limits.md) before using this on a public world.

- Sources support XS/S constructs with up to **128 non-core elements**. Nested custom modules and territory units are rejected.
- The custom shape is an element mesh; it does not behave as destructible honeycomb. The wrapper and embedded components have separate HP and repair behavior.
- Every connecting client receives the saved custom-item catalog. Large-catalog performance has not been established.
- Custom items depend on saved compiler assets and mod state. Back these up with the world, and plan carefully before removing the feature.
- Compatibility is limited to the recorded build. A second pristine server has not been tested end to end.

## Find what you need

| Guide | Covers |
|---|---|
| [**Installation**](docs/00-Installation.md) | Setup commands, server file destinations and first use. |
| [**Operations**](docs/01-Operations.md) | Settings, backups, updates and troubleshooting. |
| [**Known limits**](docs/03-Limits.md) | Damage, repair, reconnects and unsupported cases. |
| [**Development**](docs/02-Development.md) | Building from source, tests and compatibility. |
| [**Validation**](docs/04-Validation.md) | Recorded checks and what to verify on your server. |

<details>
<summary><strong>Technical reference and repository layout</strong></summary>

### Repository layout

| Folder or file | Purpose |
|---|---|
| `payload/Mods/` | Ready-to-install server mod files and configuration. |
| `source/` | C# mods, Python compiler, client UI scripts and tests. |
| `scripts/` | Installation, integrity verification and source checks. |
| `compose.yaml` | Private compiler service and persistent asset volume. |
| `compatibility.json` | Tested client version and server assembly fingerprints. |
| `manifest.json` | Public file inventory and SHA-256 checksums. |

### Implementation details

- [Creation, identity and rendering](docs/reference/01-Creation-Rendering.md)
- [Embedded elements, pickup and blueprints](docs/reference/02-Modules-Blueprints.md)
- [Persistence, recovery and diagnostics](docs/reference/03-Persistence-Diagnostics.md)
- [Source map](docs/reference/04-Source-Map.md)

</details>
