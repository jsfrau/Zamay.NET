# Zamay 2.0

Complete rewrite of the package.

## Highlights

One package targets net8.0 and net8.0-windows, with a document model shared by inspection and rendering.

## Breaking change

Version 2.0 is not API-compatible with 1.x. Use namespace Zamay for convenience output methods.

## Console

ToConsole, ToDisplayString, and async convenience APIs support Unicode text and caller-owned TextWriter instances.

## Windows inspector

ToMessageBox opens a resizable diagnostic window with an object tree, grid, details, search, Copy, and a text tab.

## SQLite

Schema browsing, counts on demand, projected pages, explicit latest ordering, record lookup by PK, and bounded TEXT/BLOB previews are available without taking ownership of the connection.

## Safety

Traversal limits, path-based cycles, sensitive-name checks before getters, and configurable exception details are included. Zamay is not a sandbox; cancellation cannot forcibly interrupt arbitrary synchronous code.

## Installation

    dotnet add package Zamay --version 2.0.0

MIT license. Publication is a separate owner action.
