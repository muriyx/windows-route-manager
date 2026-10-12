using windows_route_manager.Models;
using windows_route_manager.ViewModels;

namespace WindowsRouteManager.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("192.168.1.33/24", "192.168.1.0/24")]
    [InlineData("10.0.0.0/8", "10.0.0.0/8")]
    [InlineData("172.16.10.50/32", "172.16.10.50/32")]
    [InlineData("192.168.1.1", "192.168.1.1/32")]
    [InlineData("255.255.255.255/0", "0.0.0.0/0")]
    [InlineData("255.255.255.255/31", "255.255.255.254/31")]
    [InlineData(" 128.0.0.1/1 ", "128.0.0.0/1")]
    public void PrefixIsNormalized(string input, string expected) => Assert.Equal(expected, Ipv4Prefix.Parse(input).ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2.3.256")]
    [InlineData("1.2.3.-1")]
    [InlineData("1.2.3")]
    [InlineData("127.1")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("01.2.3.4")]
    [InlineData("1.2.3.4/33")]
    [InlineData("1.2.3.4/-1")]
    [InlineData("1.2.3.4/+1")]
    [InlineData("1.2.3.4/")]
    [InlineData("1.2.3.4/1/1")]
    [InlineData("1.2. 3.4")]
    [InlineData("::1")]
    [InlineData("1.2.3.4; Remove-Item x")]
    [InlineData("1.2.3.4/３２")]
    public void InvalidPrefixIsRejected(string? input) => Assert.False(Ipv4Prefix.TryParse(input, out _));

    [Theory]
    [InlineData("0", 0u)]
    [InlineData("65536", 65536u)]
    [InlineData("4294967295", uint.MaxValue)]
    public void VpnMetricSupportsUInt32(string input, uint expected) => Assert.Equal(expected, RouteMetric.Parse(input));

    [Theory]
    [InlineData("-1")]
    [InlineData("4294967296")]
    [InlineData("1.5")]
    [InlineData("+2")]
    [InlineData("1; exit")]
    [InlineData("")]
    public void InvalidMetricIsRejected(string input) => Assert.Throws<ArgumentException>(() => RouteMetric.Parse(input));

    [Fact]
    public void NormalizedDefaultRouteIsRecognized() => Assert.True(Ipv4Prefix.Parse("1.2.3.4/0").IsDefault);

    [Fact]
    public void ScopeIsPartOfProfileIdentity() => Assert.NotEqual(new VpnProfileId("Office", VpnScope.User), new VpnProfileId("Office", VpnScope.AllUsers));

    [Fact]
    public void EditorReportsValidationErrors()
    {
        var editor = new RouteEditorViewModel(null) { Destination = "10.0.1.3/24", Metric = "-1" };
        Assert.Null(editor.Validate());
        Assert.NotEmpty(editor.Error);
        editor.Metric = "10";
        Assert.Equal("10.0.1.0/24", editor.Validate()!.DestinationPrefix);
        Assert.Empty(editor.Error);
    }
}
