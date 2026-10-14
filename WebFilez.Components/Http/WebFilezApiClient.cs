using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WebFilez.Components.Http;

/// <summary>
/// Calls the API mapped by <c>MapWebFilezApi()</c> and turns its responses back into the results
/// the server-side services return, so the components can't tell the two apart.
/// </summary>
internal sealed class WebFilezApiClient(HttpClient http)
{
    private const string UnreachableMessage = "Couldn't reach the file service. Check your connection and try again.";

    public Task<Result<T>> GetAsync<T>(string url, JsonTypeInfo<T> responseType, CancellationToken cancellationToken) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), responseType, cancellationToken);

    public Task<Result<T>> SendAsync<TBody, T>(
        HttpMethod method,
        string url,
        TBody body,
        JsonTypeInfo<TBody> bodyType,
        JsonTypeInfo<T> responseType,
        CancellationToken cancellationToken) =>
        SendAsync(() => new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, bodyType) }, responseType, cancellationToken);

    private async Task<Result<T>> SendAsync<T>(
        Func<HttpRequestMessage> createRequest,
        JsonTypeInfo<T> responseType,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = createRequest();
            request.Headers.Add(Constants.RequestHeaderName, Constants.RequestHeaderValue);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (await ReadFailureAsync(response, cancellationToken)).To<T>();
            }

            var value = await response.Content.ReadFromJsonAsync(responseType, cancellationToken);
            return value is null
                ? Result<T>.Error("The file service sent an empty response.")
                : Result<T>.Success(value);
        }
        catch (HttpRequestException)
        {
            return Result<T>.Unavailable(UnreachableMessage);
        }
        catch (JsonException)
        {
            return Result<T>.Error("The file service sent a response that couldn't be read.");
        }
    }

    private static async Task<Result> ReadFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ProblemResponse? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync(WebFilezJsonContext.Default.ProblemResponse, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not a problem response, for example a proxy's HTML error page.
        }

        string[] messages = problem?.Detail is { Length: > 0 } detail ? [detail] : [];

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when problem?.Errors is { Count: > 0 } errors =>
                Result.Invalid([.. errors.SelectMany(e => e.Value.Select(message => new ValidationError(e.Key, message)))]),
            HttpStatusCode.NotFound => Result.NotFound(messages),
            HttpStatusCode.Conflict => Result.Conflict(messages),
            HttpStatusCode.Forbidden => Result.Forbidden(messages),
            HttpStatusCode.Unauthorized => Result.Unauthorized(messages),
            HttpStatusCode.ServiceUnavailable => Result.Unavailable(messages),
            _ => Result.Error(new ErrorList(messages)),
        };
    }
}
