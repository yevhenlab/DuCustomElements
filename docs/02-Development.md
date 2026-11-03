# Development and compatibility

## Supported environment

Validated client: **1.4.18-mydu**, unmodified. The official local ModLoader reference documents SyncLoader
support from 1.4.7, but this implementation's resource formats, UI hooks and icon refresh have only been
checked with the installed 1.4.18-mydu client. That protocol minimum is not a promise of compatibility.

The bundled DLLs target the supplied .NET 6 Orleans server. `compatibility.json` records SHA-256 hashes
and file versions of ten native dependency assemblies. The installer refuses a mismatched binary set
unless `-Rebuild` is requested. Matching hashes are a useful compatibility guard, not a full server audit.

Sensitive native dependencies include gameplay-bank imports/listeners, private bounding-box storage,
grain method signatures, direct blueprint/element SQL APIs and the client ItemInspector prototype.
Test each of these after a server/client update. This is an experimental mod with no database schema
migration requirement; it stores additional definitions through the game's existing gameplay bank.

## Build and tests

From the package root:

```powershell
$assemblies = Read-Host 'Local directory containing your effective server assemblies'
./source/build.ps1 -ServerAssemblies $assemblies
./scripts/Test-Source.ps1 -ServerAssemblies $assemblies
```

The first command builds both DLLs under `source/bin`. The second also runs native C# checks and
JavaScript tests. Supply the server implementation assemblies, not only public API stubs.
The original source's machine-specific assembly default has been removed from this package.
C# compiles with language version 10 and warnings treated as errors. The tests run on .NET 8 against
those game assemblies. Node tests use the built-in test runner and require no npm dependencies.

To build and test Python without installing Python on the host:

```powershell
docker build -t mydu-custom-elements:check ./source
$testSource = (Resolve-Path ./source).Path
docker run --rm --network none --mount "type=bind,source=$testSource,target=/tests,readonly" `
  --workdir /tests mydu-custom-elements:check python -B -m unittest -v test_shapes test_native_mesh
```

The Dockerfile uses Python 3.12 slim and pins lz4 4.4.4, numpy 2.2.6 and xxhash 3.5.0.
The base tag is not digest-pinned. Building requires access to the base-image registry and Python index;
subsequent compiler runtime needs no internet access. To install offline, prebuild/save the compiler image,
load it on the destination and use Compose without building. Preserve the exact source and dependency
versions with that image. Offline image archives are not included in this kit.

## Client-derived catalogs

The compiler needs `source/materialdefs.nqdef` copied from the matching client file
`Game/data/resources_generated/voxel/materialdefs.nqdef` (resolve against the actual installation).
The bundled file matches the validated client.

`element-resources.json` maps native asset aliases to nqdef resource subtrees used to create isolated
aliases for embedded components. It is deployed beside the DLLs. `element-icons.json` supplies native
icons to the compiler for element-only items. Both are bundled metadata, not an external runtime dependency.
They were extracted during investigation; an automated catalog extraction tool is not part of this kit.
For another client build, re-extract/validate metadata from its `resources_generated/elements/**/*.nqdef`
and icon metadata, then update both source/payload copies and the compatibility record.
Do not substitute catalogs from another client version without testing native icons and model aliases.
The full game client and server assemblies are not redistributed in this folder.

## Package maintenance

Edit source and payload JavaScript consistently. After a C# change, build and copy the resulting two DLLs
into `payload/Mods`. After changing catalogs, update their source/payload copies as appropriate.
Do not copy generated response files, test build artifacts or live registry/group/job data into a release.
The checksum manifest lists installation/source files, excluding itself and generated build outputs.

`Verify-Package.ps1` detects accidental changes to listed files. It is an integrity check, not a digital
signature. A modified release needs a regenerated `manifest.json` and a new validation record.
Read the source map before changing hooks; lifecycle and blueprint operations cross multiple files.


Publish the public project files through GitHub. Keep `.Dev Custom Elements/` private; `.gitignore` excludes it and generated build outputs. Do not force-add ignored files or commit live server configuration or world data.



