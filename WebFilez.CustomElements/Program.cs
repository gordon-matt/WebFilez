using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebFilez;
using WebFilez.Components;
using WebFilez.CustomElements;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// <wf-file-manager root="media" selection-mode="single" accept="image/*"></wf-file-manager>.
// Parameters map to kebab-case attributes; the element raises wf-navigated and wf-selected DOM events.
builder.RootComponents.RegisterCustomElement<FileManager>("wf-file-manager");

// appsettings.json is fetched from the host page's site root, so each site can point at its own API.
string apiBaseUrl = builder.Configuration["WebFilez:ApiBaseUrl"]
    ?? throw new InvalidOperationException("Set WebFilez:ApiBaseUrl in the host site's appsettings.json, e.g. https://files.example.com/api/webfilez.");

bool includeCredentials = builder.Configuration.GetValue<bool>("WebFilez:IncludeCredentials");

builder.Services.AddTransient<IncludeCredentialsHandler>();
builder.Services.AddWebFilezHttpClient(apiBaseUrl, includeCredentials, client =>
{
    if (includeCredentials)
    {
        client.AddHttpMessageHandler<IncludeCredentialsHandler>();
    }
});

await builder.Build().RunAsync();
