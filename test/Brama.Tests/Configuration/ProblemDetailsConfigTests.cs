using Brama.Configuration;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Brama.Tests.Configuration;

public class ProblemDetailsConfigTests
{
    [Fact]
    public void Every_error_kind_is_unmapped_until_it_is_mapped()
    {
        var api = new ApiConfig();

        Assert.Equal(Enum.GetValues<ErrorKind>().Length, UnmappedCount(api));
    }

    [Fact]
    public void Mapping_every_kind_leaves_nothing_unmapped()
    {
        var api = new ApiConfig();
        api.ProblemDetails(p => p
            .Map(ErrorKind.Validation, StatusCodes.Status400BadRequest)
            .Map(ErrorKind.NotFound, StatusCodes.Status404NotFound)
            .Map(ErrorKind.Conflict, StatusCodes.Status409Conflict)
            .Map(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)
            .Map(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)
            .Map(ErrorKind.Problem, StatusCodes.Status400BadRequest));

        Assert.Equal(0, UnmappedCount(api));
        Assert.Equal(StatusCodes.Status401Unauthorized, api.ProblemDetailsConfig.StatusCodes[ErrorKind.Unauthorized]);
    }

    [Fact]
    public void A_later_mapping_for_the_same_kind_wins()
    {
        var api = new ApiConfig();
        api.ProblemDetails(p => p
            .Map(ErrorKind.Problem, StatusCodes.Status400BadRequest)
            .Map(ErrorKind.Problem, StatusCodes.Status422UnprocessableEntity));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, api.ProblemDetailsConfig.StatusCodes[ErrorKind.Problem]);
    }

    [Fact]
    public void Exception_detail_is_absent_unless_a_callback_opts_in()
    {
        var api = new ApiConfig();

        Assert.Null(api.ProblemDetailsConfig.UnhandledExceptionDetail);
    }

    [Fact]
    public void Exception_detail_callback_receives_the_exception_and_the_environment()
    {
        var api = new ApiConfig();
        api.ProblemDetails(p => p.OnUnhandledException((exception, context) =>
            context.IsDevelopment ? exception.Message : "An unexpected error occurred"));

        Func<Exception, IErrorContext, string> detail = api.ProblemDetailsConfig.UnhandledExceptionDetail!;

        Assert.Equal("boom", detail(new InvalidOperationException("boom"), new FakeErrorContext(IsDevelopment: true)));
        Assert.Equal(
            "An unexpected error occurred",
            detail(new InvalidOperationException("boom"), new FakeErrorContext(IsDevelopment: false)));
    }

    [Fact]
    public void Mapping_rejects_a_status_code_outside_the_http_range()
    {
        var api = new ApiConfig();

        Assert.Throws<ArgumentOutOfRangeException>(() => api.ProblemDetails(p => p.Map(ErrorKind.NotFound, 42)));
    }

    [Fact]
    public void Localization_is_off_until_it_is_turned_on()
    {
        var api = new ApiConfig();
        Assert.False(api.ProblemDetailsConfig.LocalizationEnabled);

        api.ProblemDetails(p => p.UseLocalization());
        Assert.True(api.ProblemDetailsConfig.LocalizationEnabled);
    }

    private static int UnmappedCount(ApiConfig api) =>
        Enum.GetValues<ErrorKind>().Count(kind => !api.ProblemDetailsConfig.StatusCodes.ContainsKey(kind));

    private sealed record FakeErrorContext(bool IsDevelopment) : IErrorContext
    {
        public HttpContext HttpContext => new DefaultHttpContext();
    }
}
