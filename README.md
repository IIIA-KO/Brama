# Brama

[![CI](https://github.com/IIIA-KO/Brama/actions/workflows/ci.yml/badge.svg)](https://github.com/IIIA-KO/Brama/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Brama is a library for building REST APIs on ASP.NET Core minimal APIs with less boilerplate and with error handling that the compiler checks for you. It does not replace ASP.NET Core routing. It adds a configuration tree on top of it, plus a Roslyn source generator and an MSBuild step that catch mistakes at build time instead of in production.

> **Status: work in progress.** Brama is not on NuGet yet, and its API will change. It targets .NET 10.

## Example

```csharp
builder.Services.AddOrdersModule(); // generated from [Module], no assembly scanning

builder.Services.AddBrama(api =>
{
    api.ProblemDetails(p => p
        .Map(ErrorKind.Validation, StatusCodes.Status400BadRequest)
        .Map(ErrorKind.NotFound, StatusCodes.Status404NotFound)
        .Map(ErrorKind.Conflict, StatusCodes.Status409Conflict)
        .Map(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)
        .Map(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)
        .Map(ErrorKind.Problem, StatusCodes.Status400BadRequest));

    api.Versioning(v => v.UseUrlSegment());

    EndpointGroup orders = api.Group("Orders", g => g.Version(1)); // served under /v1/orders

    orders.MapGet<OrderResponse>(
        "/{id:guid}",
        (Guid id, IEndpointContext ctx) =>
        {
            OrderResponse? order = ctx.Get<IOrderRepository>().Find(id);

            return order is null
                ? ctx.NotFound($"No order with id {id}.")
                : ctx.Ok(order).WithLink("self", Links.GetOrder(ctx, order.Id)); // generated, type-checked link
        },
        e => e.Named("GetOrder"));
});

app.MapBrama();
```

The full sample is in [`samples/Brama.Sample.Api`](samples/Brama.Sample.Api).

## What it does

**Checked at build time**

- **Every error kind must have an HTTP status.** If an `ErrorKind` is missing from the `ProblemDetails` mapping, the build fails with `BRAMA001`, naming the missing kind. This targets a real bug: a new error type quietly falling through to the wrong status code.
- **Shipped API versions can't break by accident.** For each versioned group, the build writes a plain-text contract (endpoints, request and response fields, status mappings) to `contracts/`, and you commit it. If a later build removes or renames something in an already-shipped version, the build fails with `BRAMA010` and lists exactly what was lost. To make an intended break, you state it in code: `.AcceptBreakingChange(reason: "...")`. Adding endpoints or fields is not a break. IDE design-time builds never touch the committed contract.
- **DI registration is generated.** A `[Module]` class gets an `AddXModule()` extension method, with every implementation written out as a literal registration. No reflection runs at startup.
- **Links are typed.** Naming an endpoint generates a `Links.<Name>(...)` method, so the compiler checks link parameters and the version prefix comes from routing.

**At runtime**

- **Settings cascade through a configuration tree** (API → group → endpoint). Settings declared on a group, such as auth policy, filters, versioning, rate limits, CORS, output caching and deprecation, are inherited by everything below it. List settings append down the tree, single values override, and `Replace` clears an inherited setting for one subtree.
- **Errors use RFC 9457 `problem+json` consistently,** including validation failures with per-field errors, short-circuiting filters, authorization failures, and unhandled exceptions. Exception details are hidden unless you opt in.
- **Field-level validation** with DataAnnotations runs before the handler. Business rules stay in your code, or in FluentValidation.
- **API versioning** by URL segment (`/v1/...`).
- **Opt-in response behaviours:**
  - correlation IDs
  - request logging per group
  - rate-limit headers
  - `Deprecation` and `Sunset` headers
  - per-endpoint CORS
  - output caching
  - HATEOAS `_links`

## Why not FastEndpoints, Asp.Versioning, …?

Brama tries to add things the established libraries don't do, rather than wrap them again. Its two main ideas are settings that cascade through a tree instead of being repeated on each endpoint class, and breaking-change checks that run during `dotnet build` rather than as a separate test against a deployed service. [`docs/design.md`](docs/design.md) compares it library by library and records the design decisions.

## Project layout

| Project | Purpose |
|---|---|
| `src/Brama.Abstractions` | Public types: `Result`, `Error`, `ErrorKind`, `IEndpointContext`, `[Module]` |
| `src/Brama` | Configuration tree and runtime host (`AddBrama`, `MapBrama`) |
| `src/Brama.SourceGenerator` | Roslyn incremental generator, diagnostics, and the MSBuild contract step |
| `test/*` | Unit, generator, and end-to-end MSBuild tests |
| `samples/Brama.Sample.Api` | A small Orders API built with Brama |

## Building

```bash
dotnet test Brama.slnx
```

Requires the .NET 10 SDK; [`global.json`](global.json) sets the version.

## License

[MIT](LICENSE)
