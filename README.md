# Quotebook

A quotation and invoice tracker for small service businesses, built to show how I structure an ASP.NET Core system. It is a portfolio demo, not a product with real customers.

**Status:** work in progress. Done: domain rules, schema, sign-up/sign-in with roles, tenant isolation, customers and quotations (draft, sent, accepted, rejected). Next: quote-to-invoice, payments, dashboard, deployment.

## Stack
- .NET 10 (LTS), ASP.NET Core Razor Pages
- EF Core 10 with PostgreSQL (Neon in the demo)
- xUnit

## Structure
| Project | Job |
|---|---|
| `src/Quotebook.Core` | Entities and business rules. No framework or database code. |
| `src/Quotebook.Data` | EF Core `DbContext`, mappings, migrations. |
| `src/Quotebook.Web` | Razor Pages UI, health check, wiring. |
| `tests/Quotebook.Tests` | Unit tests for the rules. |

## Decisions
- **Payments are rows, not a "paid" flag.** Partial payments and history are kept, and the invoice status (Unpaid, Partial, Paid, Overdue) is derived from them, so it cannot drift out of date.
- **Money is `decimal`, rounded to 2 places in one helper.**
- **A quote converts to an invoice once.** Enforced in the domain and by a unique index on `Invoices.QuoteId`.
- **Stale edits are rejected, not merged.** Postgres `xmin` is the concurrency token.
- **Dates are passed in, not read from the clock,** so the rules are testable.
- **Every row carries an `OwnerId`,** and EF Core query filters scope every read to the signed-in business. `SaveChanges` stamps new rows and refuses rows for another business or with no business in scope, as a second line of defence.
- **Stale edits are caught on the database.** Editing a quotation replaces its lines, which would not change the quote row, so the edit also touches the row to force an UPDATE and move Postgres's `xmin`. A second editor holding an old copy gets a clear message instead of silently overwriting. Checked against real Postgres; an in-memory test database cannot model `xmin`.
- **Quotation numbers are per business** (Q-0001, Q-0002, ...), one past the highest in use, with a unique index as the guard against two requests racing.
- **"Today" is Malaysia's today** (fixed UTC+8), so a quote expires on the right day wherever the server runs.
- **A sign-up creates a business and its Owner.** The business id travels in the sign-in cookie as a claim. Roles are Owner (settings) and Staff (day-to-day work).
- **Passwords: length over character rules** (10+ characters, lockout after 5 failures, one message for wrong email or wrong password).
- **Pages are private by default.** A fallback authorization policy requires sign-in; public pages opt out explicitly.

## Run
```
dotnet test
dotnet run --project src/Quotebook.Web
```
Set the database with `dotnet user-secrets set "ConnectionStrings:Default" "<connection string>" --project src/Quotebook.Web`. Without it the app runs and `/health` reports healthy without a database.
