using LogVue;
using LogVue.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Serilog;
using WebFilez;
using WebFilez.Demo;
using WebFilez.Demo.Blazor.Components;
using WebFilez.Storage.AmazonS3;
using WebFilez.Storage.AzureBlob;
using WebFilez.Storage.AzureFiles;
using WebFilez.Storage.GoogleCloud;

const string EmbedCorsPolicy = "WebFilezEmbeds";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogVue(options =>
    builder.Configuration.GetSection(LogVueOptions.SectionName).Bind(options));
builder.Services.AddLogVueSqlite();

builder.Services.AddSerilog((services, configuration) => configuration
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.LogVue(services));

DemoFiles.Seed(builder.Environment.ContentRootPath);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddWebFilez(builder.Configuration);
builder.Services.AddWebFilezComponents();
builder.Services.AddWebFilezAzureBlob();
builder.Services.AddWebFilezAzureFiles();
builder.Services.AddWebFilezAmazonS3();
builder.Services.AddWebFilezGoogleCloud();

// Lets the React demo and the custom element, served from other origins, call the API.
// The upload endpoint requires the X-WebFilez header, so it has to be allowed here.
builder.Services.AddCors(options => options.AddPolicy(EmbedCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("WebFilez:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Content-Disposition")));

var app = builder.Build();

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors();

// The "media" root's public URL. Files are added at run time, so MapStaticAssets can't serve them.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "App_Data", "media")),
    RequestPath = "/media",
});

// Azurite blobs for the azure-blob root. PublicBaseUrl is /azurite/{container}.
app.MapAzuritePublicFiles();

app.UseAntiforgery();

app.MapStaticAssets();

// The demo leaves the API open. In a real app, protect it:
// app.MapWebFilezApi().RequireAuthorization();
app.MapWebFilezApi().RequireCors(EmbedCorsPolicy);

// The demo leaves the log dashboard open. In a real app, protect it:
// app.MapLogVue().RequireAuthorization();
app.MapLogVue();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();