using Velvet.Launcher.Application;
using Velvet.Launcher.Core;
using Velvet.Launcher.Infrastructure;
using Xunit;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Velvet.Launcher.UnitTests;

public sealed class LauncherServicesTests
{
    [Fact]
    public async Task Snapshot_UsesRealDependencyAndConnectivityServices()
    {
        var system = new FakeSystem();
        var snapshot = await new LauncherStatusService(new FakeDependencies(), new FakeMaster(), system, new FakeLogger(), new FakeBridge()).GetSnapshotAsync(CancellationToken.None);
        Assert.True(snapshot.Connectivity.MasterAvailable);
        Assert.True(snapshot.Dependencies.IsReady);
        Assert.Equal("test-machine", snapshot.System.ComputerName);
    }

    [Fact]
    public async Task Enrollment_StoresReturnedDeviceCredential()
    {
        var store = new FakeStore();
        var result = await new EnrollmentService(new FakeMaster(), store, new FakeInstallationIds(), new FakeSystem(), new FakeLogger()).EnrollAsync("  ABC123 ", "1.0.0", CancellationToken.None);
        Assert.Equal("device-1", result.DeviceId);
        Assert.Equal("secret-from-master", store.Value);
    }

    [Fact]
    public async Task InstallationIdStore_ReturnsSameValue()
    {
        var store = new FakeInstallationIds();
        Assert.Equal(await store.GetOrCreateAsync(CancellationToken.None), await store.GetOrCreateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MasterClient_SendsBearerAndAccepts204Heartbeat()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var store = new FakeStore { Value = "device-secret" };
        var client = CreateClient(handler, store);
        await client.SendHeartbeatAsync(new HeartbeatRequest("installation-1", "PC", "macOS", "0.1.0", DateTimeOffset.UtcNow, new(new(true), new(true, false, "Scene 1"))), CancellationToken.None);
        Assert.Equal("Bearer device-secret", handler.LastAuthorization);
        var json = JsonDocument.Parse(handler.LastBody!);
        Assert.False(json.RootElement.TryGetProperty("credential", out _));
        Assert.Equal("installation-1", json.RootElement.GetProperty("installationId").GetString());
    }

    [Fact]
    public async Task MasterClient_StopsOn401And403WithoutRetrying()
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
            var client = CreateClient(handler, new FakeStore { Value = "secret" });
            await Assert.ThrowsAsync<MasterApiException>(() => client.GetStationAsync(CancellationToken.None));
            Assert.Equal(1, handler.Calls);
            Assert.Equal(status == HttpStatusCode.Unauthorized ? MasterAuthorizationState.NotEnrolled : MasterAuthorizationState.Unauthorized, client.State.State);
        }
    }

    [Fact]
    public async Task MasterClient_Retries429AndThenSucceeds()
    {
        var handler = new RecordingHandler(call => call < 3 ? new HttpResponseMessage((HttpStatusCode)429) : new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler, new FakeStore { Value = "secret" });
        await client.SendHeartbeatAsync(new HeartbeatRequest("installation-1", "PC", "macOS", "0.1.0", DateTimeOffset.UtcNow, new(new(false), new(false, false, null))), CancellationToken.None);
        Assert.Equal(3, handler.Calls);
    }

    private static VelvetMasterClient CreateClient(RecordingHandler handler, FakeStore store) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://master.test") }, store);

    private sealed class FakeSystem : ISystemInfo { public OperatingSystemKind OperatingSystem => OperatingSystemKind.MacOS; public string ComputerName => "test-machine"; public string OperatingSystemDescription => "macOS test"; }
    private sealed class FakeDependencies : IDependencyManager { public Task<DependencyCheckResult> CheckAsync(CancellationToken _) => Task.FromResult(new DependencyCheckResult([new(DependencyKind.Chrome, true, "1", "/x", "ok")])); public Task<InstallationResult> InstallAsync(DependencyKind _, CancellationToken __) => Task.FromResult(new InstallationResult(true, "ok")); }
    private sealed class FakeMaster : IMasterClient { public MasterClientState State { get; private set; } = new(MasterAuthorizationState.Authenticated); public Task<ConnectivityStatus> CheckConnectivityAsync(CancellationToken _) => Task.FromResult(new ConnectivityStatus(true, true)); public Task<DeviceRegistration> EnrollAsync(EnrollmentRequest request, CancellationToken __) => Task.FromResult(new DeviceRegistration("device-1", "secret-from-master", "test-machine", request.InstallationId, "macOS", "1.0.0", null)); public Task<MasterStation?> GetStationAsync(CancellationToken _) => Task.FromResult<MasterStation?>(null); public Task SendHeartbeatAsync(HeartbeatRequest _, CancellationToken __) => Task.CompletedTask; }
    private sealed class FakeStore : ISecureCredentialStore { public string? Value; public Task SaveAsync(string _, string value, CancellationToken __) { Value = value; return Task.CompletedTask; } public Task<string?> GetAsync(string _, CancellationToken __) => Task.FromResult(Value); public Task DeleteAsync(string _, CancellationToken __) => Task.CompletedTask; }
    private sealed class FakeInstallationIds : IInstallationIdStore { public Task<string> GetOrCreateAsync(CancellationToken _) => Task.FromResult("installation-1"); }
    private sealed class FakeBridge : IBridgeStatusProvider { public Task<StationHeartbeatStatus> GetStatusAsync(CancellationToken _) => Task.FromResult(new StationHeartbeatStatus(new BridgeStatus(true), new ObsStatus(true, false, "Scene 1"))); }
    private sealed class FakeLogger : ILauncherLogger { public void Info(string _) { } public void Error(string _, Exception? __ = null) { } }
    private sealed class RecordingHandler(Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(Calls);
        }
    }
}
