using Jellyfin.Plugin.Kids.Api;
using Xunit;

namespace Jellyfin.Plugin.Kids.Tests;

public class KidsInfoTests
{
    [Fact]
    public void SupportedFeatures_AddSessionLimitAfterTheEarlierOnes()
    {
        Assert.Equal(["profiles", "parentLock", "allowance", "sessionLimit"], KidsInfo.SupportedFeatures);
    }
}
