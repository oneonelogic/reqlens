using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using ReqLens.Contracts;
using ReqLens.Data;
using ReqLens.Domain;
using ReqLens.Lambdas.Api;
using ReqLens.Validation;

var builder = WebApplication.CreateBuilder(args);

// Runs as a real Lambda behind API Gateway HTTP API in AWS, and as plain Kestrel locally -
// the same code path either way, which is what makes the API debuggable on a laptop.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// Resolved once at startup. Blocking here is deliberate: the app has nothing useful to do
// without a database, and failing to start is a better signal than serving 500s.
var connection = ReqLensDb.ConnectionStringAsync().GetAwaiter().GetResult();

builder.Services.AddDbContext<ReqLensDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddSingleton<RequisitionValidator>();

var app = builder.Build();

// The Blazor console is served from this same origin.
//
// MapStaticAssets, not UseStaticFiles alone: on .NET 10 the client's compiled output - every
// _framework/*.wasm - is published as endpoints in a static asset manifest rather than as files
// under a web root. UseStaticFiles finds wwwroot content such as the stylesheet and finds none
// of the framework, which presents as a page that loads and then stays blank.
// MapStaticAssets serves the client, and UseBlazorFrameworkFiles is deliberately NOT called.
//
// On .NET 10 the Blazor client's compiled output is published as routed endpoints in a static
// asset manifest, not as files under a web root. UseBlazorFrameworkFiles builds an isolated
// middleware branch for /_framework, and a branch has no endpoint middleware in it - so it
// intercepts every framework request, fails to find a file, and terminates. The symptom is a
// page that loads its stylesheet, serves its API, and never boots.
app.UseStaticFiles();
app.MapStaticAssets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ---------------------------------------------------------------------------------------------
// NO AUTHENTICATION. Every endpoint below takes the tenant as a query parameter and trusts it.
//
// That is a demo decision, stated plainly rather than hidden: the point being demonstrated is
// that tenant scoping is enforced in the data model - orders are keyed (TenantId, Id), children
// carry their own TenantId under a composite foreign key, and no query below reaches a row
// without naming its tenant. Putting Cognito in front of it changes who supplies the slug, and
// nothing else in this file.
// ---------------------------------------------------------------------------------------------

app.MapGet("/api/tenants", async (ReqLensDbContext db) =>
    Results.Ok(await db.Tenants
        .OrderBy(t => t.Name)
        .Select(t => new TenantDto(t.Id, t.Name, t.Slug))
        .ToListAsync()));

app.MapGet("/api/orders", async (string tenant, string? status, ReqLensDbContext db) =>
{
    var resolved = await Tenant(db, tenant);
    if (resolved is null) return Results.NotFound(new { error = $"No tenant '{tenant}'." });

    var query = db.Orders.Where(o => o.TenantId == resolved.Id);

    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var wanted))
            return Results.BadRequest(new { error = $"'{status}' is not an order status." });

        query = query.Where(o => o.Status == wanted);
    }

    var orders = await query
        .OrderByDescending(o => o.UpdatedAt)
        .Take(200)
        .ToListAsync();

    return Results.Ok(orders.Select(o => o.ToSummary()).ToList());
});

app.MapGet("/api/orders/{id:guid}", async (Guid id, string tenant, ReqLensDbContext db) =>
{
    var resolved = await Tenant(db, tenant);
    if (resolved is null) return Results.NotFound(new { error = $"No tenant '{tenant}'." });

    var order = await db.Orders
        .Include(o => o.Fields)
        .Include(o => o.Calls)
        .Include(o => o.Reviews)
        .FirstOrDefaultAsync(o => o.TenantId == resolved.Id && o.Id == id);

    if (order is null) return Results.NotFound();

    var fieldNames = order.Fields.ToDictionary(f => f.Id, f => f.Name);

    return Results.Ok(new OrderDetailDto(
        order.ToSummary(),
        order.ExtractionFailure,
        [.. order.Fields
            .OrderBy(f => RequisitionFields.All.ToList().IndexOf(f.Name) is var i && i < 0 ? int.MaxValue : i)
            .Select(f => f.ToDto())],
        // Chronological, not by attempt number. Telemetry rows are never deleted, so a document
        // that was re-extracted has more than one attempt 1, and ordering by attempt would
        // interleave two separate runs into a sequence that never happened.
        [.. order.Calls.OrderBy(c => c.At).ThenBy(c => c.Attempt).Select(c => c.ToDto())],
        [.. order.Reviews.OrderByDescending(r => r.At).Select(r => r.ToDto(fieldNames))]));
});

/// A short-lived presigned GET so the browser can render the original scan beside the extraction.
/// Presigned rather than proxied: the PDF never passes through this Lambda, which keeps a
/// multi-megabyte scan out of an API Gateway response.
app.MapGet("/api/orders/{id:guid}/scan-url", async (Guid id, string tenant, ReqLensDbContext db, IAmazonS3 s3) =>
{
    var resolved = await Tenant(db, tenant);
    if (resolved is null) return Results.NotFound(new { error = $"No tenant '{tenant}'." });

    var order = await db.Orders.FirstOrDefaultAsync(o => o.TenantId == resolved.Id && o.Id == id);
    if (order is null) return Results.NotFound();

    var bucket = Environment.GetEnvironmentVariable("REQUISITIONS_BUCKET")
                 ?? throw new InvalidOperationException("REQUISITIONS_BUCKET is not set.");

    var url = await s3.GetPreSignedURLAsync(new Amazon.S3.Model.GetPreSignedUrlRequest
    {
        BucketName = bucket,
        Key = order.SourceObjectKey,
        Verb = Amazon.S3.HttpVerb.GET,
        Expires = DateTime.UtcNow.AddMinutes(10),
        ResponseHeaderOverrides = { ContentType = "application/pdf" }
    });

    return Results.Ok(new { url });
});

app.MapPost("/api/orders/{id:guid}/review", async (
    Guid id,
    string tenant,
    ReviewSubmissionDto submission,
    ReqLensDbContext db,
    RequisitionValidator validator) =>
{
    var resolved = await Tenant(db, tenant);
    if (resolved is null) return Results.NotFound(new { error = $"No tenant '{tenant}'." });

    if (string.IsNullOrWhiteSpace(submission.ReviewerId))
        return Results.BadRequest(new { error = "A reviewer id is required; the audit trail has to name someone." });

    if (!Enum.TryParse<ReviewVerdict>(submission.Verdict, ignoreCase: true, out var requested))
        return Results.BadRequest(new { error = $"'{submission.Verdict}' is not a verdict." });

    var order = await db.Orders
        .Include(o => o.Fields)
        .FirstOrDefaultAsync(o => o.TenantId == resolved.Id && o.Id == id);

    if (order is null) return Results.NotFound();

    var now = DateTimeOffset.UtcNow;
    var changed = 0;

    foreach (var correction in submission.Corrections ?? [])
    {
        var field = order.Fields.FirstOrDefault(f => f.Id == correction.FieldId);
        if (field is null) continue;

        var before = field.Value;
        var after = string.IsNullOrWhiteSpace(correction.Value) ? null : correction.Value.Trim();

        if (before == after) continue;

        // One audit row per field actually changed, holding both sides. This table is the
        // overturn-rate source, so a row that records no change would inflate the metric.
        db.Reviews.Add(new ReviewAction
        {
            OrderId = order.Id,
            TenantId = order.TenantId,
            FieldId = field.Id,
            ReviewerId = submission.ReviewerId,
            Verdict = ReviewVerdict.Corrected,
            ValueBefore = before,
            ValueAfter = after,
            At = now
        });

        field.Value = after;

        // A corrected value is a human's value. It is no longer the model's claim, so the
        // model's confidence in it is meaningless and saying 1.0 would be a lie of a different
        // kind. The field is re-validated below; the confidence records who supplied it.
        field.Confidence = 1.0;
        field.SourceText = null;
        field.Grounded = null;

        changed++;
    }

    // The verdict is derived, not taken on trust. A reviewer who edits four fields and clicks
    // Approve has overturned the model four times, and the drift signal has to see that.
    var verdict = requested == ReviewVerdict.Rejected
        ? ReviewVerdict.Rejected
        : changed > 0 ? ReviewVerdict.Corrected : ReviewVerdict.Approved;

    db.Reviews.Add(new ReviewAction
    {
        OrderId = order.Id,
        TenantId = order.TenantId,
        FieldId = null,
        ReviewerId = submission.ReviewerId,
        Verdict = verdict,
        Note = submission.Note,
        At = now
    });

    if (changed > 0)
    {
        var catalog = await db.TestCatalog.Where(c => c.TenantId == resolved.Id).ToListAsync();
        var assessment = validator.Assess(order.Fields, catalog);

        order.OverallConfidence = assessment.MinConfidence;
        order.ReviewReasons = [.. assessment.ReviewReasons];
    }

    order.Status = verdict == ReviewVerdict.Rejected ? OrderStatus.Rejected : OrderStatus.Accepted;
    order.UpdatedAt = now;

    await db.SaveChangesAsync();

    return Results.Ok(new { verdict = verdict.ToString(), fieldsChanged = changed, status = order.Status.ToString() });
});

app.MapGet("/api/metrics/overturn", async (string tenant, int? days, ReqLensDbContext db) =>
{
    var resolved = await Tenant(db, tenant);
    if (resolved is null) return Results.NotFound(new { error = $"No tenant '{tenant}'." });

    var window = Math.Clamp(days ?? 7, 1, 365);
    var since = DateTimeOffset.UtcNow.AddDays(-window);

    var actions = await db.Reviews
        .Where(r => r.TenantId == resolved.Id && r.At >= since)
        .ToListAsync();

    var orderLevel = actions.Where(a => a.FieldId is null).ToList();
    var fieldLevel = actions.Where(a => a.FieldId is not null).ToList();

    var reviewedOrderIds = orderLevel.Select(a => a.OrderId).Distinct().ToList();

    // The denominator is every field a human actually looked at, which is every field on every
    // order that was reviewed - not just the ones they touched.
    var fieldsReviewed = reviewedOrderIds.Count == 0
        ? 0
        : await db.Fields.CountAsync(f => f.TenantId == resolved.Id && reviewedOrderIds.Contains(f.OrderId));

    var fieldsCorrected = fieldLevel.Select(a => a.FieldId).Distinct().Count();

    return Results.Ok(new OverturnMetricsDto(
        WindowDays: window,
        OrdersReviewed: orderLevel.Count,
        OrdersApproved: orderLevel.Count(a => a.Verdict == ReviewVerdict.Approved),
        OrdersCorrected: orderLevel.Count(a => a.Verdict == ReviewVerdict.Corrected),
        OrdersRejected: orderLevel.Count(a => a.Verdict == ReviewVerdict.Rejected),
        FieldsReviewed: fieldsReviewed,
        FieldsCorrected: fieldsCorrected,
        FieldOverturnRate: fieldsReviewed == 0 ? 0 : (double)fieldsCorrected / fieldsReviewed,
        OrderOverturnRate: orderLevel.Count == 0
            ? 0
            : (double)orderLevel.Count(a => a.Verdict != ReviewVerdict.Approved) / orderLevel.Count));
});

app.MapFallbackToFile("index.html");

app.Run();
return;

static Task<Tenant?> Tenant(ReqLensDbContext db, string slug) =>
    db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
