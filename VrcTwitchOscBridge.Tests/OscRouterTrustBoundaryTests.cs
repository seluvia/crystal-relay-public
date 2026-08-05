using System.Net;
using System.Net.Sockets;
using System.Reflection;
using VRC.OSCQuery;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class OscRouterTrustBoundaryTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.2", true)]
    [InlineData("192.168.1.20", false)]
    [InlineData("10.0.0.20", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("not-an-address", false)]
    [InlineData("", false)]
    public void LoopbackAddressValidation_RejectsNonLoopbackAndMalformedValues(
        string addressText,
        bool expected)
    {
        IPAddress? address = IPAddress.TryParse(addressText, out var parsed)
            ? parsed
            : null;

        var result = InvokeStatic<bool>(
            "IsLoopbackAddress",
            address);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.168.1.20", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", false)]
    public void OscReceiverSenderValidation_OnlyAcceptsLoopbackEndpoints(
        string addressText,
        bool expected)
    {
        var endPoint = new IPEndPoint(IPAddress.Parse(addressText), 9000);

        var result = InvokeStatic<bool>(
            "IsLoopbackEndpoint",
            endPoint);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Discovery_RejectsNonLoopbackQueryProfileBeforeNameMatching()
    {
        await using var router = new OscRouterService();
        var logs = new List<string>();
        router.LogWritten += logs.Add;

        await TryRegisterAsync(
            router,
            new OSCQueryServiceProfile(
                "VRChat",
                IPAddress.Parse("192.168.1.20"),
                -1,
                OSCQueryServiceProfile.ServiceType.OSCQuery));

        Assert.False(router.HasDiscoveredVrChat);
        Assert.Empty(logs);
    }

    [Fact]
    public void OscReceiver_BindsExplicitlyToLoopback()
    {
        var listener = InvokeStatic<UdpClient>(
            "CreateListener",
            0,
            "test listener",
            "test hint");

        using (listener)
        {
            var localEndPoint = Assert.IsType<IPEndPoint>(listener.Client.LocalEndPoint);
            Assert.Equal(IPAddress.Loopback, localEndPoint.Address);
        }
    }

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.20")]
    [InlineData("8.8.8.8")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("not-an-address")]
    public async Task Discovery_RejectsNonLoopbackOscEndpoint(string oscAddress)
    {
        using var host = CreateLoopbackOscQueryHost(oscAddress);
        await using var router = new OscRouterService();
        await TryRegisterAsync(
            router,
            new OSCQueryServiceProfile(
                "Local VRChat OSCQuery",
                IPAddress.Loopback,
                host.TcpPort,
                OSCQueryServiceProfile.ServiceType.OSCQuery));

        Assert.False(router.HasDiscoveredVrChat);
    }

    [Fact]
    public async Task MisleadingVrChatServiceName_CannotBypassEndpointValidation()
    {
        using var host = CreateLoopbackOscQueryHost("192.168.1.20");
        await using var router = new OscRouterService();
        await TryRegisterAsync(
            router,
            new OSCQueryServiceProfile(
                "VRChat trusted service",
                IPAddress.Loopback,
                host.TcpPort,
                OSCQueryServiceProfile.ServiceType.OSCQuery));

        Assert.False(router.HasDiscoveredVrChat);
    }

    [Fact]
    public async Task ValidLoopbackVrChatDiscovery_RemainsAccepted()
    {
        using var host = CreateLoopbackOscQueryHost(IPAddress.Loopback.ToString());
        await using var router = new OscRouterService();
        await TryRegisterAsync(
            router,
            new OSCQueryServiceProfile(
                "VRChat",
                IPAddress.Loopback,
                host.TcpPort,
                OSCQueryServiceProfile.ServiceType.OSCQuery));

        Assert.True(router.HasDiscoveredVrChat);
    }

    private static async Task TryRegisterAsync(
        OscRouterService router,
        OSCQueryServiceProfile profile)
    {
        var method = GetInstanceMethod("TryRegisterVrChatTargetAsync");
        var task = Assert.IsAssignableFrom<Task>(
            method.Invoke(router, new object?[] { profile, CancellationToken.None }));
        await task;
    }

    private static LoopbackOscQueryHost CreateLoopbackOscQueryHost(string oscAddress)
    {
        var tcpPort = Extensions.GetAvailableTcpPort();
        var oscQueryService = new OSCQueryServiceBuilder()
            .WithHostIP(IPAddress.Loopback)
            .WithTcpPort(tcpPort)
            .StartHttpServer()
            .Build();

        oscQueryService.HostInfo.oscIP = oscAddress;
        oscQueryService.HostInfo.oscPort = 9010;

        return new LoopbackOscQueryHost(oscQueryService, tcpPort);
    }

    private static T InvokeStatic<T>(string name, params object?[] arguments)
    {
        var method = typeof(OscRouterService).GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"Expected {name} to exist on {nameof(OscRouterService)}.");

        return (T)(method.Invoke(null, arguments)
            ?? throw new Xunit.Sdk.XunitException($"{name} returned null."));
    }

    private static MethodInfo GetInstanceMethod(string name)
    {
        return typeof(OscRouterService).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"Expected {name} to exist on {nameof(OscRouterService)}.");
    }

    private sealed class LoopbackOscQueryHost : IDisposable
    {
        public LoopbackOscQueryHost(OSCQueryService service, int tcpPort)
        {
            Service = service;
            TcpPort = tcpPort;
        }

        public OSCQueryService Service { get; }

        public int TcpPort { get; }

        public void Dispose()
        {
            Service.Dispose();
        }
    }
}
