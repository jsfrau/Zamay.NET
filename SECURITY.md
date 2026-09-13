# Security

Zamay inspects application objects and is not a sandbox. Getters, enumerators, SQLite functions, and database operations may execute code or block. See docs/safety.md before using diagnostic output with secrets or untrusted inputs.

Do not include credentials, connection strings, private database files, or personal data in public reports. Until the release repository provides a private reporting channel, contact the verified package owner through https://www.nuget.org/packages/Zamay/1.0.1/ContactOwners and share only a non-sensitive summary. Arrange a private channel before sending a reproducer containing private data.

The supported release line is 2.x. No security response SLA is promised.
