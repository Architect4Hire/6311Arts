using Arts6311.Domain.Managers.Results;

namespace Arts6311.Tests.Kernel;

public sealed class OperationResultTests
{
    [Fact]
    public void SuccessCarriesTheValueAndNoError()
    {
        var result = OperationResult.Success(42);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FailureCarriesTheErrorAndNoValue()
    {
        var error = OperationError.Of(ErrorCodes.NotFound, "No such artist.");

        var result = OperationResult.Failure<string>(error);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ValueLessResultsSucceedOrFail()
    {
        Assert.True(OperationResult.Success().Succeeded);
        Assert.False(OperationResult.Failure(OperationError.Of(ErrorCodes.Conflict, "Taken.")).Succeeded);
    }

    [Fact]
    public void ValidationErrorsAreGroupedByCamelCaseField()
    {
        var error = OperationError.Validation(
            "The artist is invalid.",
            [("DisplayName", "Required."), ("DisplayName", "Too long."), ("Slug", "Taken.")]);

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(["Required.", "Too long."], error.FieldErrors["displayName"]);
        Assert.Equal(["Taken."], error.FieldErrors["slug"]);
    }
}
