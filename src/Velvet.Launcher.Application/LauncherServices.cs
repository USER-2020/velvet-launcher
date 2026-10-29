using Velvet.Launcher.Core;

namespace Velvet.Launcher.Application;

public sealed class LauncherStatusService(
    IDependencyManager dependencies,
    IMasterClient master,
    ISystemInfo systemInfo,
    ILauncherLogger logger,
    IBridgeStatusProvider bridge)
{
    public async Task<LauncherSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dependencyTask = dependencies.CheckAsync(cancellationToken);
            var connectivityTask = master.CheckConnectivityAsync(cancellationToken);
            var bridgeTask = bridge.GetStatusAsync(cancellationToken);
            await Task.WhenAll(dependencyTask, connectivityTask, bridgeTask);
            var connectivity = connectivityTask.Result;
            MasterStation? station = null;
            if (connectivity.MasterAvailable)
            {
                station = await master.GetStationAsync(cancellationToken);
            }
            return new LauncherSnapshot(systemInfo, dependencyTask.Result, connectivity, bridgeTask.Result, station);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error("No fue posible obtener el estado del launcher.", ex);
            return new LauncherSnapshot(systemInfo,
                new DependencyCheckResult([]), new ConnectivityStatus(false, false, ex.Message), new(new(false, ex.Message), new(false, false, null, 0, 0, ex.Message)), null);
        }
    }
}

public sealed record LauncherSnapshot(
    ISystemInfo System,
    DependencyCheckResult Dependencies,
    ConnectivityStatus Connectivity,
    StationHeartbeatStatus Bridge,
    MasterStation? Station = null);

public sealed class EnrollmentService(
    IMasterClient master,
    ISecureCredentialStore credentials,
    IInstallationIdStore installationIds,
    ISystemInfo systemInfo,
    ILauncherLogger logger)
{
    private const string DeviceCredentialKey = "velvet-launcher.device-credential";

    public async Task<DeviceRegistration> EnrollAsync(string code, string launcherVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("El código es obligatorio.", nameof(code));
        var installationId = await installationIds.GetOrCreateAsync(cancellationToken);
        var registration = await master.EnrollAsync(
            new EnrollmentRequest(code.Trim(), systemInfo.ComputerName,
                systemInfo.OperatingSystemDescription, launcherVersion, installationId), cancellationToken);
        if (!string.Equals(registration.InstallationId, installationId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Master devolvió un installationId diferente al de esta instalación.");
        await credentials.SaveAsync(DeviceCredentialKey, registration.DeviceCredential, cancellationToken);
        logger.Info($"Estación enrolada: {registration.DeviceId}");
        return registration;
    }
}

public sealed class HeartbeatService(
    IMasterClient master,
    IInstallationIdStore installationIds,
    IBridgeStatusProvider bridge,
    ISystemInfo systemInfo,
    ILauncherLogger logger)
{
    public async Task SendAsync(string launcherVersion, CancellationToken cancellationToken)
    {
        if (master.State.State is MasterAuthorizationState.NotEnrolled or MasterAuthorizationState.Unauthorized)
        {
            logger.Info("Heartbeat detenido: la estación no está autenticada o autorizada.");
            return;
        }

        var installationId = await installationIds.GetOrCreateAsync(cancellationToken);
        var status = await bridge.GetStatusAsync(cancellationToken);
        await master.SendHeartbeatAsync(new HeartbeatRequest(
            installationId, systemInfo.ComputerName, systemInfo.OperatingSystemDescription,
            launcherVersion, DateTimeOffset.UtcNow, status), cancellationToken);
    }
}
