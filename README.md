# Quotebook

A quotation and invoice tracker for small service businesses, built to show how I structure an ASP.NET Core system. It is a portfolio demo, not a product with real customers.

**Status:** work in progress. Domain rules, tests and the database schema are done; login, pages and deployment are next.

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
- **Every row carries an `OwnerId`,** so one business can never see another's data.

## Run
```
dotnet test
dotnet run --project src/Quotebook.Web
```
Set the database with `dotnet user-secrets set "ConnectionStrings:Default" "<connection string>" --project src/Quotebook.Web`. Without it the app runs and `/health` reports healthy without a database.
