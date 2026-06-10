# Versioning

YmmpxLib separates three different version concepts:

- Library API version
- Internal implementation version
- Assembly metadata
- `.ymmpx` internal format compatibility version

## Library API version

`YmmpxLibraryInfo.ApiVersion` represents the public API generation of YmmpxLib.
It is meant for consumers, diagnostics, and future API compatibility checks.

```csharp
var apiVersion = YmmpxLibraryInfo.ApiVersion;
var apiVersionCode = YmmpxLibraryInfo.ApiVersionCode;
```

`YmmpxLibraryInfo.InternalVersion` is an internal identifier for implementation, diagnostics, and logging.

```csharp
var internalVersion = YmmpxLibraryInfo.InternalVersion;
var internalVersionCode = YmmpxLibraryInfo.InternalVersionCode;
```

## Assembly metadata

`YmmpxLibraryInfo.AssemblyVersion` is the version of the built .NET assembly.
It comes from .NET assembly metadata.

`YmmpxLibraryInfo.InformationalVersion` is derived from informational metadata.
NuGet, CI, and `SourceRevisionId` values may be reflected there.

These values serve different purposes from `ApiVersion` and `InternalVersion`, so they do not need to match.

## `.ymmpx` internal format compatibility

`YmmpxCompatibilityVersion` is the compatibility mode used for `.ymmpx` package creation, extraction, and internal file handling.
It is not the YmmpxLib API version.

At the moment, `Latest`, `V0_1`, and `V0_2` all resolve to the same implementation.
If `.ymmpx` ever needs a breaking internal format change, service dispatch will branch on this value.

## Notes

- `YmmpxOptions.CompatibilityVersion` controls `.ymmpx` compatibility mode only.
- `YmmpxLibraryInfo` is read-only information provided by the library.
