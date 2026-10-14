# Brama MVP Design

**Status:** Approved for planning
**Date:** 2026-08-10
**Scope:** Core MVP only. Auth/security policy, OpenAPI theming, and advanced content negotiation are separate follow-up specs (see [Deferred Scope](#deferred-scope)). HATEOAS, API versioning, and compile-time contract enforcement are in scope for MVP, all opt-in.

## Purpose

Brama is an ASP.NET library for building RESTful APIs with minimal boilerplate, strong typing, and consistent, correct error handling — without replacing ASP.NET Core's own routing engine. It sits on top of minimal APIs, adding:

- A **composable config tree** (global → group → endpoint) so cross-cutting settings (auth policy, filters, problem-details shape, versioning) are declared once and inherited, not repeated per endpoint.
- **Compile-time source generation** for request binding, dispatch, validation, and OpenAPI metadata — no runtime reflection, AOT/trim-friendly, performance on par with hand-written minimal APIs.
- **Exhaustive, compile-time-checked error-to-HTTP-status mapping** (RFC 9457 problem+json), closing a class of bugs where new error types silently fall through to the wrong status code.
- **Compile-time contract enforcement** (see [Contracts](#contracts-compile-time-breaking-change-enforcement)) — the config tree isn't only a DX convenience, it's a committed, diffable source of truth for the API surface. The build itself refuses a breaking change to an already-shipped version unless the change is explicit and reasoned in code. This is the one piece that isn't boilerplate reduction — nothing else in the .NET ecosystem does this (see [Relationship to Existing .NET Libraries](#relationship-to-existing-net-libraries)).

This design is grounded in a concrete audit of an existing production ASP.NET codebase ([Lanka.Api](#appendix-a-lanka-findings-that-shaped-this-design)) — every MVP decision below traces to a specific boilerplate pattern or bug found there, except [Contracts](#contracts-compile-time-breaking-change-enforcement), [Filters](#filters), and [Logging](#logging), which are first-principles additions motivated by gaps in the wider .NET ecosystem or in this spec itself, not patterns observed in that audit.

## Non-Goals (MVP)

- Not replacing `IEndpointRouteBuilder`/ASP.NET Core routing — Brama builds on it.
- Not providing an auth/permission system beyond wiring into ASP.NET Core's existing `AuthorizeAttribute`/policy model (separate spec).
- Not supporting non-JSON content negotiation, PATCH/JSON-Patch, ETags, idempotency keys, bulk operations, async/long-running operation patterns, or outbound HTTP (your API calling other services) — all explicitly deferred, see [Deferred Scope](#deferred-scope). HATEOAS, API versioning, compile-time contract enforcement, a request-pipeline filter mechanism, and a set of opt-in response behaviors (rate limiting, deprecation/Sunset headers, per-endpoint CORS, output caching, localized problem-details, bounded structured logging) are in scope — see [HATEOAS / Hypermedia Links](#hateoas--hypermedia-links), [API Versioning](#api-versioning), [Contracts](#contracts-compile-time-breaking-change-enforcement), [Filters](#filters), [Additional Opt-In Response Behaviors](#additional-opt-in-response-behaviors), and [Logging](#logging).

## Relationship to Existing .NET Libraries

Checked against the .NET ecosystem's established libraries (survey basis: [awesome-dotnet's API/Documentation/Validation lists](https://github.com/quozd/awesome-dotnet)) so Brama adds capability instead of re-wrapping a solved problem:

| Existing library | What it owns | Brama's relationship |
|---|---|---|
| **FastEndpoints** | REPR-pattern endpoint classes, boilerplate reduction over minimal APIs/controllers. | Closest neighbor. Brama's config tree (cascading settings across a tree, not per-endpoint-class) and [Contracts](#contracts-compile-time-breaking-change-enforcement) (compile-time compat enforcement) are the two things FastEndpoints doesn't do. Brama does not reinvent its endpoint-class ergonomics. |
| **Swashbuckle / NSwag** | OpenAPI document generation (Swashbuckle: runtime reflection; NSwag: also does client codegen as a separate downstream step). | Brama infers `Produces<T>()` at compile time via source generation as a byproduct of the config tree, not a competing OpenAPI generator. No plan to ship a Swagger UI or an OpenAPI spec writer — an existing generator can still run against Brama's generated metadata. |
| **Asp.Versioning** (Microsoft, official) | Runtime API-version resolution (URL segment, header, query string, media type), `ApiExplorer` integration. | Real overlap risk, addressed directly: Brama's [API Versioning](#api-versioning) only ships URL-segment routing baked into compile-time route generation (no runtime version resolver), and its actual differentiator is [Contracts](#contracts-compile-time-breaking-change-enforcement) — enforcing what a version *means*, not just how it's selected. If a project only needs runtime version negotiation, Asp.Versioning remains the right tool; Brama is not a drop-in replacement for it. |
| **Refit / RestSharp** | Typed HTTP client generation from an interface (Refit) or general HTTP client (RestSharp) — both consume an API, not produce one. | No overlap — Brama is server-side only in MVP. Lockstep client generation from the same compile was considered (see the earlier design discussion) but not included in MVP; noted as a natural extension of Contracts for a later spec, not duplicated work against Refit today. |
| **FluentValidation** | Full business-rule validation engine (cross-field rules, async rules, custom validators). | Brama explicitly stays out of this space — see [Validation](#validation)'s two-tier split. Brama owns only field-level DataAnnotations checks; FluentValidation (or any equivalent) remains the answer for business rules, called from the handler. |
| **Scrutor** | Runtime assembly scanning for DI registration (`AddClasses().AsImplementedInterfaces()`, etc.). | Different mechanism, adjacent goal. Brama's `[Module]` + source-generated `AddXModule()` (see [Module & DI Registration](#module--di-registration)) does the equivalent job at compile time with zero runtime reflection — not a Scrutor wrapper, a compile-time alternative to the same problem. |
| **Ocelot** | API gateway — routing/aggregation across downstream services at the edge. | No overlap — Brama operates inside a single API process, not at a gateway layer. The Lanka audit's gateway/app whitelist-duplication finding (see [Appendix A](#appendix-a-lanka-findings-that-shaped-this-design)) is noted as a future concern, not something Brama takes on. |
| **Contract-testing tools generally** (Pact, Optic, and similar — none are .NET-native or appear in the awesome-dotnet survey) | Cross-service contract verification, typically a separate test suite/CI step against a running service. | Confirms a real gap: no .NET-native, code-first, compile-time equivalent exists. This is the gap [Contracts](#contracts-compile-time-breaking-change-enforcement) fills — enforced at `dotnet build`, not a separate test run against a deployed instance. |
| **`IHttpClientFactory` / `DelegatingHandler`** (first-party ASP.NET Core), **IdentityModel's `AddClientAccessTokenHandler()`**, **Polly** | Outbound HTTP: typed clients, handler pipelines, and — specifically — fetching/caching/refreshing an OAuth token for outbound calls, plus retry/circuit-breaking. | This is exactly the "fetching JWT tokens" scenario raised during design, and it's already solved, well, first-party-adjacent. Deliberately **not** duplicated here — see [Deferred Scope](#deferred-scope). A future `Brama.Http` spec would need to out-argue this table entry specifically, not just exist. |
| **`Microsoft.Extensions.Logging` / Serilog / NLog** | Log sinks, formatting, enrichers, transport. | Not competing — see [Logging](#logging). Brama only decides *what* gets logged per config-tree node; these own everything about *how* it's written. |

## Architecture Overview

Four pieces:

1. **Config tree** — `ApiConfig` (root) → `Group` (nestable, named) → `Endpoint` (leaf). Each node holds cascaded settings: version (see [API Versioning](#api-versioning)), problem-details mapping, filters (see [Filters](#filters)), auth requirements (as ASP.NET Core policy names — no custom permission engine in MVP).
2. **Source generator** — Roslyn generator, compile-time only, **side-effect-free** (writes no files outside its own generated-source output — it runs continuously during IDE design-time builds, so anything it touches on disk would be rewritten on every keystroke). It works from **source syntax**, never from a resolved config tree (which doesn't exist until the app runs — see [Config Tree](#config-tree)). Emits:
   - Typed request binding (route/query/body/form/header → typed request object, incl. validation-attribute checks), keyed off each handler's signature
   - Dispatch call to the handler delegate/method
   - `Result<T>` → HTTP response conversion (success + problem+json on error)
   - OpenAPI `Produces<T>()` metadata inferred from generic type arguments
   - Module DI registration (`AddXModule()`) from `[Module]`-marked assemblies
   - The `BRAMA001` exhaustiveness diagnostic, from the literal `Map(ErrorKind.X, …)` calls it can see in source
   - The [Contracts](#contracts-compile-time-breaking-change-enforcement) surface model, carried out as ordinary generated source (which lands under `obj/` — untracked, safe to rewrite per keystroke) plus the `BRAMA010`/`BRAMA011` diagnostics. The generator performs no file I/O of its own: writing files from a generator is banned by Roslyn's own analyzer rules, and the generator never touches the committed contract file (see below)
3. **MSBuild target** — runs only on a real build (not IDE design-time builds), packaged inside the NuGet package, no separate CLI install. The one piece of this design that touches the git-tracked source tree: reads the generator's `obj/` surface model, diffs it against the committed per-version contract snapshot, and promotes it for [Contracts](#contracts-compile-time-breaking-change-enforcement). Kept separate from the generator specifically so a snapshot can never be silently rewritten mid-keystroke.
4. **Runtime host** — `AddBrama(...)` builds and registers the config tree; `MapBrama()` walks it, resolves each endpoint's config once, and wires the endpoints into `IEndpointRouteBuilder` along with their filters, auth policies, CORS, rate-limiting and output-caching metadata. Inline-authored `RateLimiter`/`OutputCache`/CORS policies are registered by `AddBrama(...)`, which is why the tree is built at service-registration time rather than at `MapBrama()` time. No custom routing engine.

## Config Tree

```csharp
var api = new ApiConfig()
    .ProblemDetails(p => p
        .Map(ErrorKind.Validation, StatusCodes.Status400BadRequest)
        .Map(ErrorKind.NotFound, StatusCodes.Status404NotFound)
        .Map(ErrorKind.Conflict, StatusCodes.Status409Conflict)
        .Map(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)
        .Map(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)
        .Map(ErrorKind.Problem, StatusCodes.Status400BadRequest)
        .OnUnhandledException((ex, ctx) => ctx.IsDevelopment
            ? ex.Message
            : "An unexpected error occurred"));

var orders = api.Group("Orders", g => g
    .UseFilter<AuditLogFilter>()          // list: appends to root's filters
    .RequireAuthorization("OrdersRead")); // list: appends to root's auth requirements

var orderAdmin = orders.Group("Admin", g => g
    .RequireAuthorization("OrdersWrite")  // appends -> both OrdersRead AND OrdersWrite required
    .Replace(c => c.Filters));            // escape hatch: drop inherited filters for this subtree
```

**Node types:** `ApiConfig` (root, singleton per app) → `EndpointGroup` (nestable, named, arbitrary depth) → `EndpointDefinition` (leaf, created by `MapGet/MapPost/MapPut/MapDelete/MapPatch`). A group contributes its lower-cased name as a route segment, and endpoint templates are **relative** to the segments its groups contribute — `orders.MapGet("/{id:guid}", …)` serves `/orders/{id}`. `.BaseRoute("…")` overrides just that group's own segment; `.Replace(c => c.RouteSegments)` mounts a subtree at the API root.

**Merge semantics:**
- List-valued fields (filters, auth requirements) **append** up the chain by default.
- Scalar-valued fields (e.g. a single override) **replace**.
- `.Replace(c => c.FieldName)` clears the inherited value for that field before the current node's value is applied — explicit opt-out of merge, scoped to one field.

**Resolution:** the tree is a real C# object graph, built once during startup. `MapBrama()` walks it root→leaf **once per endpoint at startup**, producing one flat resolved-config value per endpoint, and wires that value into ASP.NET Core's own endpoint metadata. No per-request tree walk; no runtime cost for the cascade once the app is serving.

**Why startup and not build time:** the tree is executable C# — `api.Group("Orders", g => g.UseFilter<AuditLogFilter>())` only produces a node once `Program.cs` runs. A source generator cannot execute it, so it cannot be the thing that resolves it. What the generator *can* see is source syntax, and that's what it's used for: per-handler binding/dispatch codegen keyed off the handler signature, the `BRAMA001` exhaustiveness check keyed off the literal `Map(ErrorKind.X, …)` calls, and the [Contracts](#contracts-compile-time-breaking-change-enforcement) surface model. An earlier draft of this spec assigned cascade resolution to the generator; that was not implementable and this line supersedes it.

## Filters

`.UseFilter<AuditLogFilter>()` appeared by example in [Config Tree](#config-tree) above but wasn't yet defined — this section is that definition, and it's also Brama's answer to inbound request transformation (header rewriting, auth-scheme normalization, correlation-ID injection, short-circuiting before the handler runs): one primitive, not two competing concepts for the same job.

- **Wraps ASP.NET Core's own `IEndpointFilter`** (`AddEndpointFilter<T>()`) — not a new execution engine. A filter can inspect/mutate the `HttpContext` and the invocation's bound arguments, call `next(ctx)` to continue the pipeline, or short-circuit by returning a `Result<T>`-shaped value directly, converted through the exact same problem-details path a handler return goes through (see [Problem Details & Error Mapping](#problem-details--error-mapping)).
- **List-valued, append cascade** — exactly like auth requirements (see [Config Tree](#config-tree)'s merge semantics): a group's filters run in addition to its parent's, root first, then group, then endpoint, wrapping the handler like an onion. `.Replace(c => c.Filters)` drops inherited filters for a subtree, the same escape hatch as every other list field.

```csharp
public class LegacyAuthHeaderFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (ctx.HttpContext.Request.Headers.TryGetValue("X-Legacy-Token", out var token))
            ctx.HttpContext.Request.Headers.Authorization = $"Bearer {token}";

        return await next(ctx);
    }
}

var orders = api.Group("Orders", g => g.UseFilter<LegacyAuthHeaderFilter>());
```

- **Outbound HTTP (your API calling other services — token fetching, retries) is a deliberately different problem, not this mechanism** — different execution model (runtime `DelegatingHandler` chain vs. this compile-time-wired filter pipeline). See [Deferred Scope](#deferred-scope).

## Endpoint Definition & Request Binding

Binding is **convention-based, not attribute-based** — no `[FromRoute]`/`[FromQuery]`/`[FromBody]`. This mirrors ASP.NET Core's own existing minimal-API binding rules rather than inventing a new scheme:

- A simple-typed handler parameter (`Guid`, `string`, `int`, enum, etc.) whose name matches a route template placeholder → bound from the route.
- A simple-typed parameter that doesn't match a route placeholder → bound from the query string.
- A single complex/record-typed parameter → bound from the request body.
- `[FromHeader]` remains as a rare, explicit escape hatch for the one case convention can't infer.

Folding route/query/body fields into one flat record (as an earlier draft of this spec did) is what created the need for per-property binding attributes in the first place — keeping them as separate handler parameters removes that ambiguity entirely, at zero attribute cost.

```csharp
public record CreateOrderBody(
    [Required, MaxLength(200)] string Description,
    bool Draft = false);

orders.MapPost<OrderResponse>(
    "/",   // relative to the group's route segment, so this is POST /orders
    async (CreateOrderBody body, IEndpointContext ctx, CancellationToken ct) =>
    {
        var order = await ctx.Get<IOrderRepo>().CreateAsync(body.CustomerId, body, ct);
        return ctx.Created(order, id: order.Id);   // Location: /orders/{newId}
    });
```

- Handler delegate parameters are **always explicitly typed** (`CreateOrderBody body`, not `var`-inferred lambda params). This is a hard convention across all Brama docs, generated scaffolds, and templates — signatures must be readable without an IDE (in a diff, in code review, on GitHub). Direct response to early design feedback that inferred-type lambdas hide the contract.
- `IEndpointContext` provides `Get<TService>()` (DI), `IsDevelopment`, and typed result helpers (`Ok`, `Created`, `NotFound`, `Error`, etc.) that all return a `Result<T>`-shaped value.

## Validation

Validation is deliberately split into two tiers, kept strictly separate so Brama never duplicates what a handler's domain/application layer already does:

- **Field-level structural validation (Brama's job)** — plain DataAnnotations attributes only (`[Required]`, `[MaxLength]`, `[Range]`, `[RegularExpression]`, etc.), checked by **generated code** before the handler runs. No custom rule engine, no `Validate()`-method convention (considered and rejected — see rationale below), no MediatR-style pipeline-behavior indirection, no reflective `ValidationFailure` construction. Failures short-circuit directly to `ErrorKind.Validation` → RFC 9457 problem+json with a per-field `errors` extension array. This cannot be miscaught and rethrown as a different error type, because there's no exception in the path at all — it's a direct generated branch.
- **Business/domain validation (explicitly not Brama's job)** — cross-field rules, business invariants, anything that belongs in a domain/application layer stays there, using whatever validation library the project already uses (FluentValidation, hand-written, etc.). The handler calls into it and returns `ctx.Error(ErrorKind.Validation, ...)`, which renders through the exact same problem-details pipeline as a binding failure. Brama guarantees the *shape* of the error response; it has no opinion on how business rules are authored or executed.

**Rejected alternative:** an `IValidatableRequest.Validate()` method on the request type was considered instead of attributes, but rejected — it invites large validation methods on request DTOs that duplicate domain/application-layer rules and effectively re-implement a rules engine (i.e., reinvents FluentValidation) inside the binding layer, which is exactly the duplication the two-tier split above is designed to avoid. Plain field-constraint attributes don't have this problem because they can only express single-field, declarative, non-business constraints.

## Problem Details & Error Mapping

- `ErrorKind` is a **closed enum** (`Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `Problem`). The source generator verifies at **build time** that every `ErrorKind` has a status-code mapping registered in `ProblemDetails(...)`; a missing mapping is a compile error (diagnostic `BRAMA001`), not a runtime fallthrough.
- Handlers return `Result<T>` (via `ctx.Ok(...)`, `ctx.Created(...)`, `ctx.Error(ErrorKind.X, detail)`, etc.). The framework converts this to the HTTP response — no per-endpoint `result.Match(Results.Ok, ApiResult.Problem)` boilerplate.
- **401/403 raised by ASP.NET Core's own authorization middleware are routed through the same mapping table** as handler-returned errors — one problem+json shape everywhere, not a separate empty-body path.
- Unhandled exceptions: `OnUnhandledException` callback controls the `Detail` string. In production, the framework does not expose `ex.Message` unless the callback explicitly chooses to return it — no accidental detail leak.

## Module & DI Registration

```csharp
using Brama;

[Module]
public partial class OrdersModule
{
    // source generator scans this assembly for endpoint groups, marker types, etc.
    // and emits AddOrdersModule(IServiceCollection) — no hand-written reflection scan per module.
}

// Program.cs
builder.Services.AddOrdersModule();
builder.Services.AddPaymentsModule();
```

- One `[Module]` marker (`Brama.Module`) per module assembly — plain name, consistent with the rest of Brama's API surface (`ApiConfig`, `Group`, `IEndpointContext`, `ErrorKind` — none of these carry a `Brama`-prefixed name either; the namespace already identifies the library). The source generator performs the assembly scan **at compile time** and emits a single `AddXModule()` extension method — replacing the hand-copied reflection-based registration block found duplicated across every module in the Lanka audit (~25 lines × 4 modules) with zero runtime reflection and one generated call per module.
- The same mechanism collects that module's endpoint groups into the config tree automatically — no separate manual `MapEndpoints()` reflection pass either.

## HATEOAS / Hypermedia Links

Included in MVP, opt-in only — zero cost and no response-shape change for endpoints that don't use it. Reconsidered from an earlier deferred-scope draft: hypermedia links are what makes an API RESTful under Fielding's own definition (Richardson Maturity Level 3), directly serving this library's founding goal ("actual RESTful APIs"), not just a nice-to-have extra.

```csharp
orders.MapGet<OrderResponse>(
    "/{id:guid}",
    async (Guid id, IEndpointContext ctx, CancellationToken ct) =>
    {
        var order = await ctx.Get<IOrderRepo>().FindAsync(id, ct);
        if (order is null) return ctx.NotFound();

        return ctx.Ok(order)
            .WithLink("self", Links.GetOrder(order.Id))
            .WithLink("cancel", Links.CancelOrder(order.Id), when: order.CanBeCancelled);
    });
```

- **`Links` is generated, not hand-written** — but only for endpoints given a literal name. The generator reads the route template and the name straight out of an endpoint declaration (`orders.MapGet<OrderResponse>("/{id:guid}", handler, e => e.Named("GetOrder"))`) and emits `Links.GetOrder(IEndpointContext context, Guid id)` with compile-checked parameters. The context is an explicit first parameter rather than ambient state: resolving a path needs the request's `LinkGenerator`, and a static accessor for it would be shared mutable state that breaks the moment two hosts run in one process — as they do in this repository's own tests. The full path is resolved at runtime through ASP.NET Core's own `LinkGenerator`, because the group prefix and version segment come from the config tree, which does not exist until the application runs (see [Config Tree](#config-tree)). What is eliminated is the magic string at the call site, not the runtime lookup — an earlier draft promised both, and could not have delivered the second.
- **Scope note:** an endpoint with no `Named(...)` gets no generated link method. `WithLink("self", url)` accepts any string, so hypermedia is available before, and without, naming anything.
- **`WithLink(rel, url, when: condition)`** attaches an entry to a `_links` extension on the response envelope. Conditional links (e.g. only show `cancel` when the resource is in a cancellable state) are expressed as plain C# — no separate hypermedia state-machine DSL to learn.
- `_links` is only present on responses where `WithLink(...)` was actually called. Endpoints that never call it produce byte-identical output to a plain `ctx.Ok(...)` — this feature cannot silently change existing response shapes. A corollary: a response body that serializes to something other than a JSON object (a bare string, a number, an array) has no member to hold `_links`, so the links are dropped and a warning is logged. Brama will not wrap the body in an envelope to make room, because that would change the response shape of an endpoint that had one already.
- **`.DisallowLinks()`** is available at the group/endpoint config-tree level for teams that want to enforce "no hypermedia in this subtree" consistency. Links attached in such a subtree are dropped and a warning is logged, rather than the request being failed: a handler disagreeing with its team's policy is a code-review problem, not a reason to return an error in production. No global on/off switch is needed otherwise, since the feature is inherently opt-in per call.

## API Versioning

Included in MVP, same opt-in/zero-cost pattern as [HATEOAS](#hateoas--hypermedia-links): if `.Version(...)` is never called anywhere in the config tree, every route is emitted exactly as it is today — no prefix, no behavior change. Pulled forward from an earlier deferred-scope draft once the config tree already had a reserved scalar slot for it (see [Config Tree](#config-tree)) and the HATEOAS pattern made "configurable, additive" a proven shape to reuse rather than invent twice.

```csharp
var api = new ApiConfig()
    .Versioning(v => v.UseUrlSegment());   // strategy chosen once, at the root

var orders = api.Group("Orders", g => g.Version(1));

var ordersV2 = api.Group("OrdersV2", g => g
    .BaseRoute("orders")
    .Version(2));

orders.MapGet<OrderResponse>("/{id:guid}", handlerV1);      // -> GET /v1/orders/{id}
ordersV2.MapGet<OrderResponse>("/{id:guid}", handlerV2);    // -> GET /v2/orders/{id}
```

- **`Version` is a scalar config-tree field** — inherits down the cascade like any other scalar (replace semantics, see [Config Tree](#config-tree)), so a whole group/subtree gets one version by setting it once at the group, individual endpoints override only when they genuinely diverge.
- **Strategy is pluggable (`IVersionStrategy`), but MVP ships exactly one built-in implementation: URL segment** (`/v{n}/...`). Chosen over header- or media-type-based negotiation because it's cache-friendly, trivially testable in a browser/curl, and matches the most common convention developers already expect. Header-based and media-type strategies are left as `IVersionStrategy` extension points for a later spec — the interface exists in MVP so adding them isn't a breaking change, but no second implementation ships now.
- **No runtime dispatch branching.** Because the URL segment strategy bakes the version straight into the route template at compile time, the source generator produces distinct route entries per version (`/v1/orders/{id}` and `/v2/orders/{id}` are just two separate generated routes) — there's no per-request "which version is this?" lookup to perform.
- Deprecation signaling (`Sunset`/`Deprecation` response headers tied to a version) builds directly on this field — see [Additional Opt-In Response Behaviors](#additional-opt-in-response-behaviors).

## Contracts: Compile-Time Breaking-Change Enforcement

This is Brama's core differentiator, not a boilerplate-reduction feature (see [Purpose](#purpose) and [Relationship to Existing .NET Libraries](#relationship-to-existing-net-libraries)). It turns [API Versioning](#api-versioning)'s `Version` field from a naming convention into an enforced compiler guarantee: **you cannot silently ship a breaking change under a version number that already shipped.**

**Mechanism, entirely inside `dotnet build` — no separate CLI tool:**

- The source generator computes the API surface per version and emits it to a file under `obj/`. **What a contract can contain is bounded by what is visible in source**, since the resolved config tree does not exist until the application runs (see [Config Tree](#config-tree)). In practice that is: each group's name and version, taken from literal `api.Group("Orders", g => g.Version(1))` declarations; each endpoint's HTTP method and route template; the response type's shape; the request types' shapes; and the `ErrorKind` → status-code mappings. A configuration assembled in a shape the generator does not recognise — built in a loop, behind a runtime conditional, seeded from `IConfiguration`, or handed to a method it cannot follow — yields `BRAMA011` rather than a contract that silently covers less than it appears to. Roslyn gives a generator no channel to hand an object to MSBuild, and `obj/` is untracked build output, so rewriting it on every keystroke is harmless. What the generator deliberately does *not* do is write the tracked snapshot: a generator that touched the tracked source tree would rewrite — or silently accept — a breaking change the instant it's typed, before a real build ever happens.
- The MSBuild target, which runs only on a real build, reads that `obj/` model and promotes it to a committed, git-tracked file — `contracts/<GroupName>.v<N>.contract.txt` — and diffs it against what's already there.
  - **Format: one self-contained fact per line, sorted.** Not JSON, which an earlier draft specified. Each line carries its own full path (`response.field  GET /orders/{id}  Status : string`), which makes the compatibility rule exact and trivial to state: **a change is breaking if any committed line is absent from the new build.** Nested JSON cannot support that rule — a field is one fragment of a line shared with its siblings, so adding a field and removing one look identical at line level, and separating them needs a bespoke structural differ. The flat form also makes the pull-request diff say, in words, precisely what broke, which is the entire reason for committing the snapshot.
  - **No committed file yet for this version** (first real build after a `.Version(N)` is introduced) → the target writes it. No error — the version has just frozen.
  - **Committed file exists and the current surface differs** in a breaking way (removed/renamed field, narrowed type, removed endpoint, changed status-code mapping) → **the build fails**, and the generator surfaces diagnostic `BRAMA010` in the IDE listing the exact diff (the target does the diffing; the diagnostic is how it's shown at the code location, e.g. the `.Version(N)` call). Non-breaking additions (new optional field, new endpoint under the same version) do not fail the build and update the committed file automatically.
- **Intentional breaking change to an already-frozen version:** add `.AcceptBreakingChange(reason: "...")` on that version's group node in the config tree — plain C#, so it shows up in the same PR diff as the code change that causes it, self-documenting and reviewable, not a side-channel flag or a CLI invocation. One real rebuild with that call present rewrites the committed contract file to match the new surface; the call is then typically removed (transient, the same way running `npm install` rewrites a lockfile — not a permanent marker left in the tree).
- **The config tree must be statically analyzable** for any of this to work — both the generator's surface computation and the MSBuild target depend on resolving `.Group(...)`/`.Version(...)`/`.Map*(...)` calls at compile time. A tree built inside a loop, behind a runtime conditional, or seeded from `IConfiguration`/environment values can't be resolved this way. When the generator can't statically resolve a config-tree node, it reports diagnostic `BRAMA011` ("config tree must be statically analyzable for Contracts") rather than silently skipping that node's contract check — a silent skip would be worse than no Contracts feature at all, since it would look enforced without being enforced.

```csharp
var ordersV1 = api.Group("OrdersV1", g => g
    .Version(1)
    .AcceptBreakingChange(reason: "Dropping legacy `LegacyStatus` field, replaced by `Status` in this same version per ADR-042."));
```

- **Why per-version files, not one repo-wide file:** matches the versioning model already locked — a version is the unit that's supposed to stay stable, so it's the unit that gets frozen and diffed. Deleting a version's group from the config tree (a deliberate retirement) requires deleting its contract file too, keeping the two in sync by construction rather than by discipline.
- **Scope of "breaking":** structural surface only (shape, routes, status mappings) — Brama has no visibility into runtime behavior or business-logic changes, and does not claim to catch those. This is a compile-time structural contract, not a full API-behavior regression suite. One consequence worth stating plainly: adding a **required** request field is a breaking change for clients but appears here as an addition, because required-ness is not part of the recorded surface.
- **Opt-in like everything else in this spec:** a group with no `Version(...)` set never gets a contract file and is never diffed — Contracts only activates once versioning is turned on, so it costs nothing for projects that don't use either.

## Additional Opt-In Response Behaviors

Six more config-tree fields, all following the same pattern as [HATEOAS](#hateoas--hypermedia-links) and [API Versioning](#api-versioning): declared once (root, group, or endpoint), inherited down the cascade like any other config-tree field, and invisible on any endpoint that doesn't set them. None require touching Request Binding, Validation, or the `Result<T>` → HTTP status mapping.

**Rate limiting.** Brama does not implement a rate limiter — that's ASP.NET Core's `AddRateLimiter` engine. Two levels of involvement, both optional:
- **Reference an externally-registered policy** — `.RequireRateLimiting("orders-write")`, unchanged from plain ASP.NET Core, for teams that already configure `AddRateLimiter` themselves.
- **Or author the policy inline in the tree** — `.RateLimit(permitLimit: 100, window: TimeSpan.FromMinutes(1))` at group/endpoint level; the generator emits the equivalent `AddRateLimiter` policy registration, so the policy definition lives next to the endpoints it protects instead of a separate `Program.cs` block.
- `.EmitRateLimitHeaders()` writes the IETF-draft `RateLimit-Limit` and `RateLimit-Reset` headers on the way out — purely informational, opt-in per node, and only for an **inline-authored** policy, since that is the only case where Brama knows the limit. `RateLimit-Remaining` is **not** emitted: it needs the live partition's lease count, which ASP.NET Core's rate limiter does not expose to an endpoint filter, and reconstructing it from a parallel counter would drift from the limiter actually doing the enforcing. An earlier draft of this spec promised all three "sourced from the policy's own lease metadata"; that was not achievable without Brama reimplementing the limiter, and this supersedes it.

```csharp
orders.Group("Writes", g => g
    .RateLimit(permitLimit: 100, window: TimeSpan.FromMinutes(1))
    .EmitRateLimitHeaders());
```

**Sunset/deprecation headers.** Extends the `Version` field already in the config tree (see [API Versioning](#api-versioning)) with two more scalars: `.Deprecated()` and `.Sunset(DateOnly)`. When set, generated code writes RFC 8594 `Deprecation` and `Sunset` response headers automatically — no separate middleware to register.

```csharp
var ordersV1 = api.Group("OrdersV1", g => g
    .Version(1)
    .Deprecated()
    .Sunset(new DateOnly(2027, 1, 1)));
```

**Per-endpoint CORS.** `.Cors(policy => ...)` at group/endpoint level, list-valued like filters (append semantics — a subgroup adds to, doesn't replace, its parent's allowed origins) with `.Replace(c => c.Cors)` as the same escape hatch already defined for filters. Wired to ASP.NET Core's existing named-`CorsPolicy`/`RequireCors()` mechanism, not a reimplementation.

**Output caching.** `.OutputCache(duration: TimeSpan)` at endpoint or group level, a thin wrapper over ASP.NET Core's built-in `OutputCache` middleware (`CacheOutput()`), applied per resolved config-tree node rather than requiring a separate `[OutputCache]` attribute or manual policy registration per endpoint.

**Localized problem-details messages.** Extends `ProblemDetails(...)` (see [Problem Details & Error Mapping](#problem-details--error-mapping)) with an opt-in `.UseLocalization()` call; when enabled, `Detail` strings are resolved through `IStringLocalizer` keyed by `ErrorKind` and respect the request's `Accept-Language`, falling back to the existing hardcoded string when no translation is registered. Off by default — the mapping table behaves exactly as already specified until this is turned on.

## Logging

Bounded deliberately: **Brama decides what gets logged per config-tree node; it does not own sinks, formatting, or transport.** `Microsoft.Extensions.Logging`, Serilog, and NLog already solve that, and staying out of it is what keeps this section from growing without limit.

- **`.LogRequests()`** at group/endpoint level — one structured `ILogger` entry per request through that node (method, route, resolved version, status code, duration), emitted through whatever `ILogger`/sink the host app already has wired up. Off by default; nothing is logged that Brama itself doesn't already know from routing the request.
- **`.LogLevel(LogLevel)`** — scalar, replace-cascade like [API Versioning](#api-versioning)'s `Version` field — overrides the effective log level for that subtree's request-logging entries, without touching the host app's global logging configuration.
- **Correlation-ID propagation** — `.PropagateCorrelationId(headerName: "X-Correlation-Id")` reads (or generates, if absent) a correlation ID on the way in, attaches it to the `ILogger` scope for the duration of the request, and echoes it on the way out. Not traced to a specific Lanka finding (the audit noted logging/tracing as a general cross-cutting gap, not an itemized row) — included because it's the one piece of request logging that's genuinely awkward to hand-roll correctly (scope lifetime, echoing on error paths too) and cheap to give a one-line declarative form.

```csharp
var orders = api.Group("Orders", g => g
    .LogRequests()
    .PropagateCorrelationId());
```

## Deferred Scope

Everything in this list was evaluated against one question: **does adding it change a decision already locked above, or does it sit on top of one, or does it have to out-argue an incumbent library on its own merits?** That split — not "too much scope" — is why some items stay deferred while HATEOAS, versioning, Contracts, Filters, the six response behaviors, and Logging were all pulled in.

**Not merely additive — each of these would force re-opening a section already locked, or depends on infrastructure Brama doesn't own:**
- **Content negotiation beyond JSON** (XML, Protobuf, SSE, streaming) — [Endpoint Definition & Request Binding](#endpoint-definition--request-binding) locks "one complex param = body," which in practice means "body is JSON." Non-JSON formats would redesign that binding convention and the [Problem Details](#problem-details--error-mapping) response shape at the same time, not add to either.
- **PATCH / JSON-Patch / JSON-Merge-Patch** — needs a request model that isn't "a record you bind whole." Partial-update semantics conflict with the `required`-member structural-validation story in [Validation](#validation), which assumes a complete object.
- **ETags / conditional requests** (`If-Match`, `If-None-Match`) — needs a resource-version source, which lives in persistence, not the API layer. Brama would be specifying behavior it can't itself guarantee without dictating a storage contract.
- **Idempotency keys** for HTTP writes — same shape of problem as ETags: requires storing seen keys somewhere, a hard infrastructure dependency the framework doesn't provide.
- **Bulk operations** — multi-status (207) responses don't fit the one-status-code `Result<T>` → HTTP mapping locked in [Problem Details](#problem-details--error-mapping).
- **Standardized async/long-running operation pattern** (202 + `Location` + polling resource) — needs a status-resource convention, i.e. a new endpoint shape, not a config-tree addition.
- **Auth/permission system** beyond ASP.NET Core's existing policy model — security-critical enough to deserve its own dedicated design pass rather than being folded in here.
- **Outbound HTTP (your API calling other services — fetching/caching/refreshing a token, retries, a simplified `DelegatingHandler`-style wrapper)** — a genuinely different execution model from [Filters](#filters) (runtime `DelegatingHandler` chain vs. compile-time-wired pipeline), and has to out-argue IdentityModel's `AddClientAccessTokenHandler()`, Polly, and `IHttpClientFactory` on its own merits (see [Relationship to Existing .NET Libraries](#relationship-to-existing-net-libraries)) — that argument deserves its own spec (tentatively `Brama.Http`), not a paragraph bolted onto this one.

These are not oversights — they were identified via the Lanka.Api audit as real, currently-unsolved problems, and are being deliberately sequenced rather than bolted onto the MVP.

## Project Structure

```
Brama/
├── Brama.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── Brama.Abstractions/      # interfaces/attributes consumers compile against (net10.0)
│   ├── Brama/                   # config tree, group/endpoint API, runtime host (net10.0)
│   └── Brama.SourceGenerator/   # Roslyn generator (netstandard2.0 — required by Roslyn hosting)
│       └── build/               # MSBuild target packaged into the NuGet as buildTransitive/*.targets — see Contracts
├── test/
│   ├── Brama.Tests/
│   ├── Brama.SourceGenerator.Tests/
│   └── Brama.SourceGenerator.MSBuildTests/  # fixture-project integration tests for the Contracts MSBuild target
└── samples/
    └── Brama.Sample.Api/        # runnable ASP.NET Core app exercising the library end-to-end
        └── contracts/            # committed per-version contract snapshots, see Contracts
            └── Orders.v1.contract.txt
```

Dependency direction: `samples/test → Brama → Brama.Abstractions`. `Brama.SourceGenerator` is referenced by consuming projects as an analyzer (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`), not a normal library dependency. Every project using [Contracts](#contracts-compile-time-breaking-change-enforcement) gets its own `contracts/` directory next to the `.csproj` that declares the versioned groups — committed to source control like a lockfile.

## Testing Strategy

- `Brama.Tests` — unit tests for config-tree merge semantics (list-append vs scalar-replace vs `.Replace()`), problem-details mapping resolution, `Result<T>` conversion, [Filters](#filters) ordering (root→group→endpoint onion, short-circuit behavior).
- `Brama.SourceGenerator.Tests` — snapshot/golden-file tests asserting generated code for representative endpoint shapes (route/query/body binding combinations, validation failure paths, missing-`ErrorKind`-mapping diagnostic), plus the `BRAMA011` diagnostic for a non-statically-analyzable config tree.
- **`Brama.SourceGenerator.MSBuildTests`** — integration tests that write a throwaway project to disk and run `dotnet build` against it, so the target is driven exactly as a consumer drives it: first-real-build baseline write, non-breaking-change pass, breaking-change failure with correct `BRAMA010` diff content, `.AcceptBreakingChange(...)` correctly rewriting the committed file, and — specifically — that a design-time build (IDE-style, no real `dotnet build`) does *not* touch the committed contract file, verifying the generator/target split in [Architecture Overview](#architecture-overview) actually holds.
- `Brama.Sample.Api` — manual/integration smoke target; not a substitute for the above, but exercises the full request pipeline (including [Logging](#logging) and rate-limit policy authoring) against a real ASP.NET Core host.

## Appendix A: Lanka Findings That Shaped This Design

Audit of `IIIA-KO/Lanka` (`src/Api/Lanka.Api`, minimal-API "vertical slice" host, ~73 endpoint classes, no MVC controllers):

| Finding | Brama MVP response |
|---|---|
| `ApiResult.Problem`'s `ErrorType → HTTP status` switch has no `Unauthorized` case — ~17 real handlers return HTTP 500 with a self-contradicting problem-details body (401 type URI, 500 detail). | Closed `ErrorKind` enum + build-time exhaustiveness check (`BRAMA001`) — missing mapping is a compile error, not a silent fallthrough. |
| Three independent, inconsistent error-to-response paths (`Result.Match`, `GlobalExceptionHandler` with its own bypass of `IProblemDetailsService`, raw empty-body 401/403). | Single problem-details mapping table used for handler errors *and* auth-middleware 401/403. |
| `GlobalExceptionHandler` puts raw `exception.Message` into `Detail` in all environments (not gated to dev). | `OnUnhandledException` callback is the only path to expose exception detail; production default never includes it. |
| Validation exceptions from non-`Result` handlers get miscaught by an outer exception-handling behavior and surface as generic 500 instead of 400. | Validation runs as generated pre-handler code with a direct `ErrorKind.Validation` branch — no exception layer to miscatch through. |
| `result.Match(Results.Ok, ApiResult.Problem)` — identical 2-line dispatch on ~every endpoint. | Handler returns `Result<T>`-shaped value directly; framework performs the conversion. |
| `.WithTags().WithName().WithSummary().WithDescription()` — identical 4-line OpenAPI tail on all 73 endpoints; zero `Produces<T>()`/`[ProducesResponseType]` anywhere, so OpenAPI schemas are accidental. | Generator infers `Produces<T>()` from generic type args and tags/name from group/route; typed response declaration is the default, not opt-in. |
| Identical DI-registration block (`AddDomainEventHandlers`/`AddIntegrationEventHandlers`, ~25 lines) hand-copied across 4 modules. | `[Module]` marker + generated `AddXModule()` — one generated call per module, zero hand-written reflection scan. |
| No API versioning at all. | Addressed in MVP: opt-in URL-segment versioning, `IVersionStrategy`-pluggable for future header/media-type schemes (see [API Versioning](#api-versioning)). |
| No rate-limit response headers, no Sunset/deprecation headers, no per-endpoint CORS, no output caching, no localized problem-details. | Addressed in MVP as opt-in response behaviors (see [Additional Opt-In Response Behaviors](#additional-opt-in-response-behaviors)). |
| No content negotiation beyond JSON, no PATCH, no ETags, no idempotency keys, no bulk operations, no standardized async/long-running pattern, inconsistent pagination. | Explicitly deferred (see [Deferred Scope](#deferred-scope)) — real, confirmed gaps, sequenced into follow-up specs rather than bolted onto MVP. |
| Public-endpoint whitelist duplicated by hand in two places (endpoint `AllowAnonymous()` + Gateway YARP config) — drift risk. | Noted for the auth/security follow-up spec; MVP does not yet address multi-layer routing topologies (gateway + app). |
