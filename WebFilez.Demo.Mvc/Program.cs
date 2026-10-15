using Microsoft.Extensions.FileProviders;
using WebFilez;
using WebFilez.Demo;
using WebFilez.Storage.AmazonS3;
using WebFilez.Storage.AzureBlob;
using WebFilez.Storage.AzureFiles;
using WebFilez.Storage.GoogleCloud;

var builder = WebApplication.CreateBuilder(args);

DemoFiles.Seed(builder.Environment.ContentRootPath);

builder.Services.AddControllersWithViews();

// The file manager is a Blazor component, rendered into the MVC views over a Blazor Server circuit.
builder.Services.AddServerSideBlazor();

builder.Services.AddWebFilez(builder.Configuration);
builder.Services.AddWebFilezAzureBlob();
builder.Services.AddWebFilezAzureFiles();
builder.Services.AddWebFilezAmazonS3();
builder.Services.AddWebFilezGoogleCloud();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// The "media" root's public URL. Files are added at run time, so MapStaticAssets can't serve them.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "App_Data", "media")),
    RequestPath = "/media",
});

app.UseRouting();

app.MapStaticAssets();
app.MapControllers();
app.MapBlazorHub();

// Uploads, downloads and thumbnails always go through the API, even when the component runs
// in-process. In a real app, protect it: app.MapWebFilezApi().RequireAuthorization();
app.MapWebFilezApi();

app.Run();