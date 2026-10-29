using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Velvet.Launcher.Core;

namespace Velvet.Launcher.Infrastructure;

public sealed class SystemInfo : ISystemInfo
{
    public OperatingSystemKind OperatingSystem => System.OperatingSystem.IsWindows() ? OperatingSystemKind.Windows :
        System.OperatingSystem.IsMacOS() ? OperatingSystemKind.MacOS : System.OperatingSystem.IsLinux() ? OperatingSystemKind.Linux : OperatingSystemKind.Unknown;
    public string ComputerName => Environment.MachineName;
    public string OperatingSystemDescription => RuntimeInformation.OSDescription;
}

public sealed class FileLogger(string directory) : ILauncherLogger
{
    private readonly object gate = new();
    private string Path => System.IO.Path.Combine(directory, "launcher.log");
    public void Info(string message) => Write("INFO", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);
    private void Write(string level, string message, Exception? exception)
    {
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path, $"{DateTimeOffset.UtcNow:O} [{level}] {message} {exception}\n");
        }
    }
}

public sealed class LocalDependencyManager(ILauncherLogger logger) : IDependencyManager
{
    private static readonly string[] ChromeWindows = [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "/Google/Chrome/Application/chrome.exe",
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Google/Chrome/Application/chrome.exe"];

    public Task<DependencyCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new[] { DetectChrome(), DetectObs(), DetectMeshCentralAgent(), DetectBridge() };
        return Task.FromResult(new DependencyCheckResult(values));
    }

    public Task<InstallationResult> InstallAsync(DependencyKind dependency, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsMacOS())
        {
            var package = dependency switch
            {
                DependencyKind.Chrome => "google-chrome",
                DependencyKind.ObsStudio => "obs",
                _ => null,
            };
            return package is null
                ? Task.FromResult(new InstallationResult(false, "Esta dependencia no se instala desde el setup."))
                : RunInstallerAsync("brew", ["install", "--cask", package], cancellationToken);
        }

        if (OperatingSystem.IsWindows())
        {
            var package = dependency switch
            {
                DependencyKind.Chrome => "Google.Chrome",
                DependencyKind.ObsStudio => "OBSProject.OBSStudio",
                _ => null,
            };
            return package is null
                ? Task.FromResult(new InstallationResult(false, "Esta dependencia no se instala desde el setup."))
                : RunInstallerAsync("winget", ["install", "--id", package, "-e", "--silent", "--accept-source-agreements", "--accept-package-agreements"], cancellationToken);
        }

        return Task.FromResult(new InstallationResult(false, "Instalación automática no disponible en este sistema."));
    }

    private async Task<InstallationResult> RunInstallerAsync(string command, string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return new(false, $"No se pudo iniciar {command}.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode == 0) return new(true, "Instalación completada.");
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            logger.Error($"Falló la instalación de una dependencia: {error}");
            return new(false, string.IsNullOrWhiteSpace(error) ? $"{command} terminó con código {process.ExitCode}." : error.Trim());
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(false, $"No se encontró {command}. Instálalo y vuelve a intentarlo.");
        }
    }

    private static DependencyStatus DetectChrome()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = ChromeWindows.FirstOrDefault(File.Exists);
            return FromExecutable(DependencyKind.Chrome, path);
        }
        if (OperatingSystem.IsMacOS()) return FromExecutable(DependencyKind.Chrome, "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
        return FromCommand(DependencyKind.Chrome, "google-chrome");
    }

    private static DependencyStatus DetectObs()
    {
        if (OperatingSystem.IsWindows()) return FromCommand(DependencyKind.ObsStudio, "obs64");
        if (OperatingSystem.IsMacOS()) return FromExecutable(DependencyKind.ObsStudio, "/Applications/OBS.app/Contents/MacOS/OBS");
        return FromCommand(DependencyKind.ObsStudio, "obs");
    }

    private static DependencyStatus DetectMeshCentralAgent()
    {
        if (OperatingSystem.IsMacOS())
        {
            var path = new[]
            {
                "/usr/local/mesh_services/meshagent",
                "/Library/mesh_services/meshagent",
                "/usr/local/bin/meshagent",
            }.FirstOrDefault(File.Exists);
            return FromExecutable(DependencyKind.MeshCentralAgent, path);
        }

        if (OperatingSystem.IsWindows())
        {
            var paths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "/Mesh Agent/meshagent.exe",
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "/MeshCentral Agent/meshagent.exe",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Mesh Agent/meshagent.exe",
            };
            return FromExecutable(DependencyKind.MeshCentralAgent, paths.FirstOrDefault(File.Exists));
        }

        return FromCommand(DependencyKind.MeshCentralAgent, "meshagent");
    }

    private static DependencyStatus DetectBridge() =>
        FromExecutable(DependencyKind.VelvetBridge,
            Environment.GetEnvironmentVariable("VELVET_BRIDGE_PATH") ?? string.Empty);

    private static DependencyStatus FromExecutable(DependencyKind kind, string? path)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        return new(kind, exists, exists ? TryVersion(path!) : null, exists ? path : null, exists ? "Detectado" : "No detectado");
    }

    private static DependencyStatus FromCommand(DependencyKind kind, string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("which", command) { RedirectStandardOutput = true, UseShellExecute = false });
            if (process is null) return new(kind, false, null, null, "No detectado");
            process.WaitForExit(1000);
            var path = process.StandardOutput.ReadToEnd().Trim();
            return FromExecutable(kind, path);
        }
        catch { return new(kind, false, null, null, "No detectado"); }
    }

    private static string? TryVersion(string path)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(path, "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            if (p is null) return null;
            p.WaitForExit(1000);
            return p.StandardOutput.ReadToEnd().Trim();
        }
        catch { return null; }
    }
}

public sealed class KeychainCredentialStore : ISecureCredentialStore
{
    private const string Service = "co.thevelvetstudio.launcher";
    public async Task SaveAsync(string key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsMacOS()) await RunSecurityAsync("add-generic-password", ["-a", key, "-s", Service, "-w", value, "-U"], cancellationToken);
        else if (OperatingSystem.IsWindows()) await WindowsCredential.WriteAsync(key, value, cancellationToken);
        else throw new PlatformNotSupportedException("Almacenamiento seguro no implementado para este sistema.");
    }
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS()) return await RunSecurityAsync("find-generic-password", ["-a", key, "-s", Service, "-w"], cancellationToken);
        if (OperatingSystem.IsWindows()) return await WindowsCredential.ReadAsync(key, cancellationToken);
        throw new PlatformNotSupportedException();
    }
    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS()) await RunSecurityAsync("delete-generic-password", ["-a", key, "-s", Service], cancellationToken);
        else if (OperatingSystem.IsWindows()) await WindowsCredential.DeleteAsync(key, cancellationToken);
    }
    private static async Task<string?> RunSecurityAsync(string command, string[] args, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(command);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        using var p = Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo iniciar Keychain.");
        await p.WaitForExitAsync(token);
        if (p.ExitCode != 0) return null;
        return (await p.StandardOutput.ReadToEndAsync(token)).Trim();
    }
}

public sealed class LocalInstallationIdStore : IInstallationIdStore
{
    private readonly string path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VelvetLauncher", "installation-id");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                var existing = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
                if (Guid.TryParse(existing, out _)) return existing;
            }

            var id = Guid.NewGuid().ToString("D");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, id, cancellationToken);
            return id;
        }
        finally { gate.Release(); }
    }
}

internal static class WindowsCredential
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Blob { public int Size; public nint Data; }
    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, ref Blob output);
    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, ref Blob output);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint handle);

    private static string FilePath(string key) => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VelvetLauncher", Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).Replace('/', '_'));
    public static Task WriteAsync(string key, string value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var inputBytes = Encoding.UTF8.GetBytes(value);
        var input = new Blob { Size = inputBytes.Length, Data = Marshal.AllocHGlobal(inputBytes.Length) };
        Marshal.Copy(inputBytes, 0, input.Data, inputBytes.Length);
        try
        {
            var output = new Blob();
            if (!CryptProtectData(ref input, "VELVET Launcher credential", 0, 0, 0, 0, ref output)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var encrypted = new byte[output.Size]; Marshal.Copy(output.Data, encrypted, 0, encrypted.Length);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath(key))!); File.WriteAllBytes(FilePath(key), encrypted);
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
        return Task.CompletedTask;
    }
    public static Task<string?> ReadAsync(string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var path = FilePath(key); if (!File.Exists(path)) return Task.FromResult<string?>(null);
        var encrypted = File.ReadAllBytes(path); var input = new Blob { Size = encrypted.Length, Data = Marshal.AllocHGlobal(encrypted.Length) }; Marshal.Copy(encrypted, 0, input.Data, encrypted.Length);
        try
        {
            var output = new Blob(); if (!CryptUnprotectData(ref input, 0, 0, 0, 0, 0, ref output)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var clear = new byte[output.Size]; Marshal.Copy(output.Data, clear, 0, clear.Length); return Task.FromResult<string?>(Encoding.UTF8.GetString(clear)); }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
    public static Task DeleteAsync(string key, CancellationToken token) { token.ThrowIfCancellationRequested(); if (File.Exists(FilePath(key))) File.Delete(FilePath(key)); return Task.CompletedTask; }
}

public sealed class VelvetMasterClient : IMasterClient
{
    private const string CredentialKey = "velvet-launcher.device-credential";
    private readonly HttpClient httpClient;
    private readonly ISecureCredentialStore credentials;
    private readonly ILauncherLogger? logger;
    public MasterClientState State { get; private set; } = new(MasterAuthorizationState.Unknown);

    public VelvetMasterClient(HttpClient httpClient, ISecureCredentialStore credentials, ILauncherLogger? logger = null)
    {
        if (httpClient.BaseAddress is null || httpClient.BaseAddress.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Master debe utilizar HTTPS.", nameof(httpClient));
        this.httpClient = httpClient;
        this.credentials = credentials;
        this.logger = logger;
    }

    public async Task<ConnectivityStatus> CheckConnectivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credential = await credentials.GetAsync(CredentialKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(credential))
            {
                State = new(MasterAuthorizationState.NotEnrolled, "La estación no está enrolada.");
                return new(true, false, State.Message);
            }
            _ = await GetStationAsync(cancellationToken);
            return new(true, true);
        }
        catch (MasterApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new(true, false, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            State = new(MasterAuthorizationState.NetworkUnavailable, ex.Message);
            return new(false, false, ex.Message);
        }
    }

    public async Task<DeviceRegistration> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "/api/launcher/enrollment")
            { Content = JsonContent.Create(request) }, cancellationToken);
        await EnsureExpectedAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<DeviceRegistration>(cancellationToken: cancellationToken);
        if (result is null) throw new InvalidOperationException("Master devolvió un registro vacío.");
        State = new(MasterAuthorizationState.Authenticated);
        return result;
    }

    public async Task<MasterStation?> GetStationAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/launcher/station", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureExpectedAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MasterStation>(cancellationToken: cancellationToken);
    }

    public async Task SendHeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(
            HttpMethod.Post, "/api/launcher/heartbeat", JsonContent.Create(request), cancellationToken);
        // Master acepta 204 como heartbeat válido; cualquier 2xx también es válido para compatibilidad.
        await EnsureExpectedAsync(response, cancellationToken);
        State = new(MasterAuthorizationState.Authenticated);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var credential = await credentials.GetAsync(CredentialKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(credential))
        {
            State = new(MasterAuthorizationState.NotEnrolled, "La estación no está enrolada.");
            throw new MasterApiException(HttpStatusCode.Unauthorized, State.Message!);
        }
        return await SendWithRetryAsync(() =>
        {
            var request = new HttpRequestMessage(method, path) { Content = content is null ? null : CloneContent(content) };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential);
            return request;
        }, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = requestFactory();
                var response = await httpClient.SendAsync(request, cancellationToken);
                if (!ShouldRetry(response.StatusCode) || attempt == maxAttempts) return response;
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }
        throw new InvalidOperationException("No se pudo completar la solicitud a Master.");
    }

    private async Task EnsureExpectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var status = response.StatusCode;
        if (status == HttpStatusCode.Unauthorized)
        {
            State = new(MasterAuthorizationState.NotEnrolled, "La estación no está autenticada. Debe enrolarse nuevamente.");
            throw new MasterApiException(status, State.Message!);
        }
        if (status == HttpStatusCode.Forbidden)
        {
            State = new(MasterAuthorizationState.Unauthorized, "La estación no está autorizada o fue revocada.");
            throw new MasterApiException(status, State.Message!);
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger?.Error($"Master respondió {(int)status}: {body}");
        var detail = body.Length > 240 ? body[..240] : body;
        throw new MasterApiException(status, string.IsNullOrWhiteSpace(detail)
            ? $"Master respondió {(int)status}."
            : $"Master respondió {(int)status}: {detail}");
    }

    private static bool ShouldRetry(HttpStatusCode status) => status == HttpStatusCode.RequestTimeout ||
        status == (HttpStatusCode)429 || (int)status >= 500;

    private static HttpContent CloneContent(HttpContent source)
    {
        var bytes = source.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        var content = new ByteArrayContent(bytes);
        foreach (var header in source.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return content;
    }
}

public sealed class LocalBridgeStatusProvider : IBridgeStatusProvider
{
    private readonly string socketPath = Environment.GetEnvironmentVariable("VELVET_BRIDGE_SOCKET") ?? "/private/tmp/velvet-launcher-bridge.sock";
    private const string PipeName = "velvet-launcher-bridge";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StationHeartbeatStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await ConnectAsync(cancellationToken);
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var request = new BridgeWireRequest(Guid.NewGuid().ToString(), "GetStreamStatus", null, DateTimeOffset.UtcNow);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            await writer.FlushAsync(cancellationToken);
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) return Disconnected();
            var response = JsonSerializer.Deserialize<BridgeWireResponse>(line, JsonOptions);
            if (response is null || !response.Success || response.Data is null) return new(new BridgeStatus(false, response?.Message), new ObsStatus(false, false, null, 0, 0, response?.Message));
            var status = response.Data.Value.Deserialize<BridgeWireStatus>(JsonOptions);
            return status is null ? Disconnected() : new(new BridgeStatus(true), new ObsStatus(status.Connected, status.Streaming, status.CurrentScene, status.BitrateKbps ?? 0, status.DroppedFrames ?? 0, status.Error));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return Disconnected(); }
    }

    private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        if (System.OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000, cancellationToken);
            return pipe;
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static StationHeartbeatStatus Disconnected() => new(new BridgeStatus(false, "Bridge IPC no disponible."), new ObsStatus(false, false, null, 0, 0, "Bridge IPC no disponible."));
    private sealed record BridgeWireRequest(string OperationId, string Operation, object? Payload, DateTimeOffset Timestamp);
    private sealed record BridgeWireResponse(string OperationId, bool Success, string? Message, JsonElement? Data);
    private sealed record BridgeWireStatus(bool Connected, bool Streaming, string? CurrentScene, double? BitrateKbps, long? DroppedFrames, string? Error);
}
