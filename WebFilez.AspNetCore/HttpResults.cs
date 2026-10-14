namespace WebFilez.AspNetCore;

/// <summary>Turns service results into responses: values as JSON, failures as problem details.</summary>
internal static class HttpResults
{
    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Failure(result);

    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Failure(result);

    public static IResult Failure(Ardalis.Result.IResult result)
    {
        if (result.Status == ResultStatus.Invalid)
        {
            var grouped = (result.ValidationErrors ?? [])
                .GroupBy(e => e.Identifier ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            return Results.ValidationProblem(grouped);
        }

        int statusCode = result.Status switch
        {
            ResultStatus.NotFound => StatusCodes.Status404NotFound,
            ResultStatus.Conflict => StatusCodes.Status409Conflict,
            ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
            ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
            ResultStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
            ResultStatus.CriticalError => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest,
        };

        string? detail = result.Errors?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        return Results.Problem(detail: detail, statusCode: statusCode);
    }
}
