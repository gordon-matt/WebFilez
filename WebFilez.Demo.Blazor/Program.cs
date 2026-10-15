using Microsoft.Extensions.FileProviders;
using WebFilez;
using WebFilez.Demo;
using WebFilez.Demo.Blazor.Components;
using WebFilez.Storage.AmazonS3;
using WebFilez.Storage.AzureBlob;
using WebFilez.Storage.AzureFiles;
using WebFilez.Storage.GoogleCloud;

const string EmbedCorsPolicy = "WebFilezEmbeds";

var builder = WebApplication.CreateBuilder(args);

DemoFiles.Seed(builder.Environment.ContentRootPath);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddWebFilez(builder.Configuration);
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

app.UseAntiforgery();

app.MapStaticAssets();

// The demo leaves the API open. In a real app, protect it:
// app.MapWebFilezApi().RequireAuthorization();
app.MapWebFilezApi().RequireCors(EmbedCorsPolicy);

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();