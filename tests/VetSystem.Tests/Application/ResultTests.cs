using FluentAssertions;
using VetSystem.Application.Common;

namespace VetSystem.Tests.Application;

public class ResultTests
{
    [Fact]
    public void Success_CarriesValueAndNoError()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_CarriesErrorAndForbidsValueRead()
    {
        var result = Result.Failure<int>(Error.NotFound("Foo", 1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("not_found");
        Action read = () => _ = result.Value;
        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validation_ProducesFieldErrors()
    {
        var fieldErrors = new Dictionary<string, string[]>
        {
            ["Email"] = ["Email is required."],
        };

        var error = Error.Validation(fieldErrors);

        error.Code.Should().Be("validation_failed");
        error.FieldErrors.Should().NotBeNull();
        error.FieldErrors!["Email"].Should().ContainSingle().Which.Should().Be("Email is required.");
    }
}
