using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ReqLens.Web;
using ReqLens.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API is served from the same origin as this app - the ReqLens.Lambdas.Api host serves both -
// so the base address needs no configuration and there is no CORS to get wrong.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ReqLensApi>();
builder.Services.AddScoped<TenantContext>();

await builder.Build().RunAsync();
