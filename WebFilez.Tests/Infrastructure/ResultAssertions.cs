namespace WebFilez.Tests.Infrastructure;

public static class ResultAssertions
{
    public static T ShouldSucceed<T>(this Result<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but got {result.Status}: {result.GetDisplayMessage()}");
        return result.Value;
    }

    public static void ShouldSucceed(this Result result) =>
        Assert.True(result.IsSuccess, $"Expected success but got {result.Status}: {result.GetDisplayMessage()}");
}