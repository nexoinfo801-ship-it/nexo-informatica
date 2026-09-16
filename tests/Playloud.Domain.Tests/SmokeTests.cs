using Xunit;

namespace Playloud.Domain.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Domain_Assembly_Is_Loadable()
    {
        Assert.NotNull(typeof(Playloud.Domain.Common.EntityId<object>).Assembly);
    }
}
