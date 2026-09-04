using System.Net.Http.Json;
using ReqLens.Contracts;

namespace ReqLens.Web.Services;

/// <summary>
/// The console's view of the API.
/// </summary>
/// <remarks>
/// Every call names a tenant, because every endpoint requires one. There is no ambient tenant and
/// no default: a screen that forgets to pass one fails loudly rather than quietly showing another
/// clinic's queue.
/// </remarks>
public sealed class ReqLensApi(HttpClient http)
{
    public async Task<IReadOnlyList<TenantDto>> TenantsAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<List<TenantDto>>("api/tenants", ct) ?? [];

    public async Task<IReadOnlyList<OrderSummaryDto>> OrdersAsync(
        string tenant, string? status = null, CancellationToken ct = default)
    {
        var url = $"api/orders?tenant={Uri.EscapeDataString(tenant)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";

        return await http.GetFromJsonAsync<List<OrderSummaryDto>>(url, ct) ?? [];
    }

    public Task<OrderDetailDto?> OrderAsync(string tenant, Guid id, CancellationToken ct = default)
        => http.GetFromJsonAsync<OrderDetailDto>(
            $"api/orders/{id}?tenant={Uri.EscapeDataString(tenant)}", ct);

    public async Task<string?> ScanUrlAsync(string tenant, Guid id, CancellationToken ct = default)
    {
        var response = await http.GetFromJsonAsync<ScanUrl>(
            $"api/orders/{id}/scan-url?tenant={Uri.EscapeDataString(tenant)}", ct);

        return response?.Url;
    }

    public async Task<ReviewResult?> SubmitReviewAsync(
        string tenant, Guid id, ReviewSubmissionDto submission, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            $"api/orders/{id}/review?tenant={Uri.EscapeDataString(tenant)}", submission, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ReviewResult>(ct);
    }

    public Task<OverturnMetricsDto?> OverturnAsync(string tenant, int days = 7, CancellationToken ct = default)
        => http.GetFromJsonAsync<OverturnMetricsDto>(
            $"api/metrics/overturn?tenant={Uri.EscapeDataString(tenant)}&days={days}", ct);

    private sealed record ScanUrl(string Url);

    public sealed record ReviewResult(string Verdict, int FieldsChanged, string Status);
}

/// <summary>
/// The clinic currently being reviewed. Held in one place so that switching tenants reloads
/// every screen, rather than leaving one of them showing the previous clinic's data.
/// </summary>
public sealed class TenantContext
{
    private string? _slug;

    public event Action? Changed;

    public IReadOnlyList<TenantDto> Available { get; set; } = [];

    public string? Slug
    {
        get => _slug;
        set
        {
            if (_slug == value) return;
            _slug = value;
            Changed?.Invoke();
        }
    }

    public string Require() => Slug ?? throw new InvalidOperationException("No tenant is selected.");
}
