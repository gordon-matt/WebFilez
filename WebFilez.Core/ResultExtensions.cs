namespace WebFilez;

/// <summary>
/// Converts a failed result between value types while preserving why it failed.
/// </summary>
/// <remarks>
/// Nearly every filesystem method opens by resolving a path and returning early if that fails.
/// Without this, each one would need its own <c>ResultStatus</c> switch, and any status missed in
/// one of them would silently downgrade a 403 or a 404 into a generic 500.
/// </remarks>
public static class ResultExtensions
{
    public static bool IsSuccessStatus(this IResult result) =>
        result.Status is ResultStatus.Ok or ResultStatus.Created or ResultStatus.NoContent;

    /// <summary>
    /// Re-shapes a failed <paramref name="result"/> as a <see cref="Result{TTo}"/>, carrying the
    /// status, error messages and validation errors across. Throws on a success, since there
    /// would be no value to carry.
    /// </summary>
    public static Result<TTo> To<TTo>(this IResult result)
    {
        if (result.IsSuccessStatus())
        {
            throw new InvalidOperationException(
                $"{nameof(To)} propagates failures; a successful result cannot be converted to " +
                $"Result<{typeof(TTo).Name}> without a projection.");
        }

        string[] errors = result.Errors?.ToArray() ?? [];

        return result.Status switch
        {
            ResultStatus.NotFound => Result<TTo>.NotFound(errors),
            ResultStatus.Forbidden => Result<TTo>.Forbidden(errors),
            ResultStatus.Unauthorized => Result<TTo>.Unauthorized(errors),
            ResultStatus.Invalid => Result<TTo>.Invalid([.. result.ValidationErrors ?? []]),
            ResultStatus.Conflict => Result<TTo>.Conflict(errors),
            ResultStatus.Unavailable => Result<TTo>.Unavailable(errors),
            ResultStatus.CriticalError => Result<TTo>.CriticalError(errors),
            _ => Result<TTo>.Error(new ErrorList(errors)),
        };
    }

    /// <summary>Same as <see cref="To{TTo}"/> but for the non-generic <see cref="Result"/>.</summary>
    public static Result ToVoid(this IResult result)
    {
        if (result.IsSuccessStatus())
        {
            return Result.Success();
        }

        string[] errors = result.Errors?.ToArray() ?? [];

        return result.Status switch
        {
            ResultStatus.NotFound => Result.NotFound(errors),
            ResultStatus.Forbidden => Result.Forbidden(errors),
            ResultStatus.Unauthorized => Result.Unauthorized(errors),
            ResultStatus.Invalid => Result.Invalid([.. result.ValidationErrors ?? []]),
            ResultStatus.Conflict => Result.Conflict(errors),
            ResultStatus.Unavailable => Result.Unavailable(errors),
            ResultStatus.CriticalError => Result.CriticalError(errors),
            _ => Result.Error(new ErrorList(errors)),
        };
    }

    /// <summary>
    /// The message to surface to a user, falling back to a status-appropriate default so the UI
    /// never renders an empty alert.
    /// </summary>
    public static string GetDisplayMessage(this IResult result)
    {
        string? first = result.Errors?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        var validation = result.ValidationErrors?.FirstOrDefault();
        if (validation is not null && !string.IsNullOrWhiteSpace(validation.ErrorMessage))
        {
            return validation.ErrorMessage;
        }

        return result.Status switch
        {
            ResultStatus.NotFound => "That item no longer exists.",
            ResultStatus.Forbidden => "You do not have permission to do that.",
            ResultStatus.Unauthorized => "Please sign in and try again.",
            ResultStatus.Conflict => "That conflicts with something that already exists.",
            ResultStatus.Invalid => "Some of the values supplied were not valid.",
            ResultStatus.Unavailable => "The file manager is temporarily unavailable.",
            _ => "Something went wrong. Please try again.",
        };
    }
}
