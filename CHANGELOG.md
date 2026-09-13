# Changelog

## 2.0.0

Zamay 2.0 is a complete rewrite of the original package.

- Added a renderer-independent inspection document and text rendering to a caller-owned TextWriter.
- Added a Windows diagnostic dialog with object trees, tables, details, search, and copying.
- Added a SQLite browser with schema metadata, explicit counts, projected pages, bounded previews, and primary-key record lookup.
- Added traversal budgets, path-based cycle detection, sensitive-name checks before getters, and configurable exception details.
- Preserved Unicode text while escaping control characters and truncating without splitting surrogate pairs.
- Added custom inspector registration, reusable type/reflection caches, and opt-in bounded root async enumeration.
- Added unit, database integration, UI smoke, and package-consumer validation.
- Packaged console, SQLite, and optional Windows APIs in one multi-target Zamay package.

### Breaking changes

Version 2.0 is not API-compatible with Zamay 1.x. The public convenience namespace is Zamay; no adapters for the former API are included.
