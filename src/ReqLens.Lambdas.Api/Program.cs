using Amazon.Lambda.AspNetCoreServer.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Runs as a real Lambda behind API Gateway HTTP API in AWS, and as plain Kestrel locally -
// the same code path either way, which is what makes the API debuggable on a laptop.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
