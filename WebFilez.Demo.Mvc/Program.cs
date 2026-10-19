using LogVue;
using LogVue.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Serilog;
using WebFilez;
using WebFilez.Demo;
using WebFilez.Storage.AmazonS3;
using WebFilez.Storage.AzureBlob;
using WebFilez.Storage.AzureFiles;
using WebFilez.Storage.GoogleCloud;

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

builder.Services.AddControllersWithViews();

// The file manager is a Blazor component, rendered into the MVC views over a Blazor Server circuit.
builder.Services.AddServerSideBlazor();

builder.Services.AddWebFilez(builder.Configuration);
builder.Services.AddWebFilezComponents();
builder.Services.AddWebFilezAzureBlob();
builder.Services.AddWebFilezAzureFiles();
builder.Services.AddWebFilezAmazonS3();
builder.Services.AddWebFilezGoogleCloud();

var app = builder.Build();

app.UseSerilogRequestLogging();

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

// The demo leaves the log dashboard open. In a real app, protect it:
// app.MapLogVue().RequireAuthorization();
app.MapLogVue();

app.Run();