# Changelog

## 1.0.0 - 2026-06-12

### Added
- YMMPX package creation.
- YMMPX extraction and FilePath restoration.
- `links.json` / `manifest.json` / `links.txt` compatible link map loading.
- YMM4 shared library plugin package.

### Changed
- Release package name is now fixed as `YmmpxLibPlugin.ymme`.
- Versioning is managed through GitHub Releases and assembly metadata.

### Fixed
- Restricted project marker resolution to project files extracted from the current package.
- Restricted link definitions and linked resources to files extracted from the current package.
- Added archive size, entry count, and compression ratio limits.
- Preserved preexisting extraction files when an archive entry has the same name.
- Preserved existing output files when package creation fails or is cancelled.
- Deduplicated equivalent resource paths and ignored non-string `FilePath` values.
- Fixed mojibake in generated public XML documentation.
- Removed the known `MSB3277` warning from plugin builds.
- Required release builds to pass the test suite before creating release artifacts.
- Made dependency audits fail CI when vulnerable or deprecated packages are detected.
- Enforced SemVer release tags and derived GitHub prerelease status from the tag.
- Removed file-name-only path restoration to prevent matching unrelated resources.
- Repacked highly compressible generated packages without compression when required by extraction limits.
- Rejected archive entries that could target NTFS alternate data streams.
- Avoided file/directory cross-kind collisions in available-path helpers.

### Compatibility
- Public API is treated as stable from 1.0.0.
