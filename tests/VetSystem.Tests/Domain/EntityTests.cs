using FluentAssertions;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Domain;

public class EntityTests
{
    private sealed class FakeEntity : Entity
    {
    }

    [Fact]
    public void IsDeleted_TrueWhen_DeletedAtSet()
    {
        var e = new FakeEntity { DeletedAt = DateTimeOffset.UtcNow };

        e.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void IsDeleted_FalseWhen_DeletedAtNull()
    {
        var e = new FakeEntity();

        e.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void GuidV7Generator_ProducesVersion7Guids()
    {
        var generator = new GuidV7Generator();

        var ids = Enumerable.Range(0, 10).Select(_ => generator.New()).ToArray();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().AllSatisfy(id =>
        {
            var bytes = id.ToByteArray();
            var version = (bytes[7] & 0xF0) >> 4;
            // .NET emits Guid v7 in big-endian for the time fields; check the literal byte that
            // holds the RFC 9562 version nibble.
            (version == 7 || (bytes[6] & 0xF0) >> 4 == 7).Should().BeTrue();
        });
    }

    [Fact]
    public void EnvironmentMode_LimitedToSoloAndPartnership()
    {
        EnvironmentMode.All.Should().BeEquivalentTo(["solo", "partnership"]);
    }
}
