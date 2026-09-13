# Contributing

Use .NET 8 and start with docs/building.md. Windows is required for the full solution and UI smoke tests. The Core and SQLite tests can run on Linux.

Keep inspection separate from rendering. New inspectors must respect budgets, sensitive-name checks, ownership, and cancellation. Add behavior tests for changed contracts. Database changes need integration tests that execute the generated SQL, including quoted identifiers and parameter values.

Do not add a public API solely for a test. Changes to the shipped API should include XML documentation where ownership, lifetime, side effects, or error behavior is not obvious, and update the relevant documentation. Self-describing record members do not need repetitive summaries.

Run eng/Validate.ps1 and eng/Test-Package.ps1 before proposing a release change. Do not publish packages as part of ordinary development. The release repository location has not yet been configured; contribution routing will follow that repository once it is public.
