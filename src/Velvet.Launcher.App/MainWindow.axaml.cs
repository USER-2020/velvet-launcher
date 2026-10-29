using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Velvet.Launcher.Application;
using Velvet.Launcher.Core;
using Velvet.Launcher.Infrastructure;
using System.Reflection;

namespace Velvet.Launcher.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly LauncherStatusService statusService;
    private readonly string masterUrl;
    private string dependencySummary = "Comprobando…";
    private string masterStatus = "Comprobando…";
    private string lastMessage = "";
    private string bridgeStatus = "Comprobando…";
    private string obsStatus = "Comprobando…";
    private string enrollmentCode = "";
    private string toastMessage = "";
    private bool toastVisible;
    private bool meshCentralMissing;
    private bool refreshing;
    private bool stationRegistered;
    private bool appsStarted;
    private readonly EnrollmentService enrollmentService;
    private readonly IDependencyManager dependencies;
    private readonly ISecureCredentialStore credentials;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public string StationName { get; } = Environment.MachineName;
    public string OperatingSystem { get; } = new SystemInfo().OperatingSystemDescription;
    public string DependencySummary { get => dependencySummary; private set => Set(ref dependencySummary, value); }
    public string MasterStatus { get => masterStatus; private set => Set(ref masterStatus, value); }
    public string LastMessage { get => lastMessage; private set => Set(ref lastMessage, value); }
    public string BridgeStatus { get => bridgeStatus; private set => Set(ref bridgeStatus, value); }
    public string ObsStatus { get => obsStatus; private set => Set(ref obsStatus, value); }
    public string EnrollmentCode { get => enrollmentCode; set => Set(ref enrollmentCode, value); }
    public string ToastMessage { get => toastMessage; private set => Set(ref toastMessage, value); }
    public bool ToastVisible { get => toastVisible; private set => Set(ref toastVisible, value); }
    public bool MeshCentralMissing { get => meshCentralMissing; private set => Set(ref meshCentralMissing, value); }
    public bool Refreshing { get => refreshing; private set => Set(ref refreshing, value); }
    public bool StationRegistered { get => stationRegistered; private set { Set(ref stationRegistered, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NeedsEnrollment))); } }
    public bool NeedsEnrollment => !StationRegistered;

    public MainWindow()
    {
        InitializeComponent();
        var logger = new FileLogger(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VelvetLauncher"));
        var systemInfo = new SystemInfo();
        using var unused = new HttpClient();
        masterUrl = Environment.GetEnvironmentVariable("VELVET_MASTER_URL") ?? "https://alaska-promise-taxes-pillow.trycloudflare.com";
        var http = new HttpClient { BaseAddress = new Uri(masterUrl), Timeout = TimeSpan.FromSeconds(10) };
        credentials = new KeychainCredentialStore();
        var master = new VelvetMasterClient(http, credentials, logger);
        dependencies = new LocalDependencyManager(logger);
        statusService = new LauncherStatusService(dependencies, master, systemInfo, logger, new LocalBridgeStatusProvider());
        enrollmentService = new EnrollmentService(master, credentials, new LocalInstallationIdStore(), systemInfo, logger);
        DataContext = this;
        _ = RefreshAsync();
    }
    private async void Refresh(object? sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Enroll(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(EnrollmentCode)) { LastMessage = "Pega el código temporal generado en Station Manager."; return; }
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.1.0";
            var result = await enrollmentService.EnrollAsync(EnrollmentCode, version, CancellationToken.None);
            EnrollmentCode = "";
            LastMessage = "La estación ya está registrada. Puedes continuar desde Master.";
            await ShowToastAsync($"Estación enrolada correctamente: {result.DeviceId}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LastMessage = "Revisa el código y la conexión con Master.";
            await ShowToastAsync($"No se pudo enrolar: {ex.Message}", true);
        }
    }
    private async void InstallMissing(object? sender, RoutedEventArgs e)
    {
        await ShowToastAsync("Instalando Chrome y OBS. Puede solicitar permisos del sistema.");
        foreach (var dependency in new[] { DependencyKind.Chrome, DependencyKind.ObsStudio })
        {
            var status = (await dependencies.CheckAsync(CancellationToken.None)).Dependencies.FirstOrDefault(x => x.Kind == dependency);
            if (status?.IsInstalled == true) continue;
            var result = await dependencies.InstallAsync(dependency, CancellationToken.None);
            if (!result.Succeeded)
            {
                await ShowToastAsync(result.Message, true);
                return;
            }
        }
        await ShowToastAsync("Chrome y OBS están listos.");
        await RefreshAsync();
    }
    private async Task ShowToastAsync(string message, bool isError = false)
    {
        ToastMessage = message;
        ToastVisible = true;
        if (isError) LastMessage = message;
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (ToastMessage == message) ToastVisible = false;
    }
    private async Task RefreshAsync()
    {
        if (Refreshing) return;
        Refreshing = true;
        try
        {
            var snapshot = await statusService.GetSnapshotAsync(CancellationToken.None);
            MasterStatus = snapshot.Connectivity.MasterAvailable ? "Conectado" : "Sin conexión";
            StationRegistered = snapshot.Station is not null || !string.IsNullOrWhiteSpace(await credentials.GetAsync("velvet-launcher.device-credential", CancellationToken.None));
            if (snapshot.Connectivity.Error?.Contains("no está autenticada", StringComparison.OrdinalIgnoreCase) == true)
                StationRegistered = false;
            var requiredDependencies = snapshot.Dependencies.Dependencies
                .Where(x => x.Kind is DependencyKind.Chrome or DependencyKind.ObsStudio or DependencyKind.MeshCentralAgent)
                .ToArray();
            DependencySummary = requiredDependencies.Length == 0 ? "No se pudo comprobar" :
                string.Join("  ·  ", requiredDependencies.Select(x => $"{(x.IsInstalled ? "✓" : "✗")} {x.Kind}"));
            MeshCentralMissing = requiredDependencies.FirstOrDefault(x => x.Kind == DependencyKind.MeshCentralAgent)?.IsInstalled != true;
            LastMessage = snapshot.Connectivity.Error is null ? "Estado actualizado." : snapshot.Connectivity.Error;
            BridgeStatus = "No requerido en esta fase";
            ObsStatus = "Se verificará localmente";
            if (!string.IsNullOrWhiteSpace(snapshot.Station?.RoomName) && !appsStarted)
            {
                StartRequiredApplications();
                appsStarted = true;
            }
        }
        finally
        {
            Refreshing = false;
        }
    }
    private void OpenMaster(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(masterUrl) { UseShellExecute = true });
    private void OpenMeshCentralDownload(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://meshcentral.com/") { UseShellExecute = true });
    private void StartRequiredApplications()
    {
        try
        {
            if (System.OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open") { UseShellExecute = false, ArgumentList = { "-a", "Google Chrome", masterUrl } });
                Process.Start(new ProcessStartInfo("open") { UseShellExecute = false, ArgumentList = { "-a", "OBS" } });
            }
            else if (System.OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(masterUrl) { UseShellExecute = true });
                Process.Start(new ProcessStartInfo("obs64.exe") { UseShellExecute = true });
            }
        }
        catch (Exception exception)
        {
            LastMessage = $"La estación está lista, pero no se pudieron abrir las aplicaciones: {exception.Message}";
        }
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); } }
}
