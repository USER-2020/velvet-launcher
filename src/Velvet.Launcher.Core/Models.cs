using System.Net;
using System.Text.Json;

namespace Velvet.Launcher.Core;

public enum DependencyKind { Chrome, ObsStudio, MeshCentralAgent, VelvetBridge }
public enum OperatingSystemKind { Windows, MacOS, Linux, Unknown }

public sealed record DependencyStatus(
    DependencyKind Kind, bool IsInstalled, string? Version, string? Path, string? Message);

public sealed record DependencyCheckResult(IReadOnlyList<DependencyStatus> Dependencies)
{
    public bool IsReady => Dependencies.All(x => x.IsInstalled);
}

public sealed record InstallationResult(bool Succeeded, string Message);
public sealed record ConnectivityStatus(bool InternetAvailable, bool MasterAvailable, string? Error = null);
public sealed record DeviceRegistration(
    string DeviceId, string DeviceCredential, string ComputerName,
    string InstallationId, string OperatingSystem, string LauncherVersion, string? RoomName);

public sealed record EnrollmentRequest(string Code, string ComputerName, string OperatingSystem, string LauncherVersion, string InstallationId);
public sealed record MasterStation(
    string DeviceId, string InstallationId, string ComputerName, string OperatingSystem,
    string LauncherVersion, string? StudioId, string? RoomName,
    JsonElement? Assignment, JsonElement? Configuration);

public sealed record BridgeStatus(bool Connected, string? Error = null);
public sealed record ObsStatus(bool Connected, bool Streaming, string? CurrentScene, double BitrateKbps = 0, long DroppedFrames = 0, string? Error = null);
public sealed record StationHeartbeatStatus(BridgeStatus Bridge, ObsStatus Obs);
public sealed record HeartbeatRequest(
    string InstallationId, string ComputerName, string OperatingSystem,
    string LauncherVersion, DateTimeOffset SentAt, StationHeartbeatStatus Status);

public enum MasterAuthorizationState { Unknown, Authenticated, NotEnrolled, Unauthorized, NetworkUnavailable }
public sealed record MasterClientState(MasterAuthorizationState State, string? Message = null);
public sealed class MasterApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public interface IDependencyManager
{
    Task<DependencyCheckResult> CheckAsync(CancellationToken cancellationToken);
    Task<InstallationResult> InstallAsync(DependencyKind dependency, CancellationToken cancellationToken);
}

public interface IMasterClient
{
    Task<ConnectivityStatus> CheckConnectivityAsync(CancellationToken cancellationToken);
    Task<DeviceRegistration> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken);
    Task<MasterStation?> GetStationAsync(CancellationToken cancellationToken);
    Task SendHeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken);
    MasterClientState State { get; }
}

public interface IInstallationIdStore
{
    Task<string> GetOrCreateAsync(CancellationToken cancellationToken);
}

public interface IBridgeStatusProvider
{
    Task<StationHeartbeatStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public interface ISecureCredentialStore
{
    Task SaveAsync(string key, string value, CancellationToken cancellationToken);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public interface ISystemInfo
{
    OperatingSystemKind OperatingSystem { get; }
    string ComputerName { get; }
    string OperatingSystemDescription { get; }
}

public interface ILauncherLogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}
