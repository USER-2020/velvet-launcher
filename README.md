# VELVET Launcher

MVP multiplataforma de escritorio para preparar y enrolar estaciones de THE VELVET STUDIO. El checkout actual es greenfield: no contiene VELVET Master, VELVET Agent ni VELVET Bridge.

## Requisitos

- .NET SDK 10.0.402 o posterior.
- Acceso a NuGet para restaurar Avalonia y xUnit.
- Windows 10/11 o macOS compatible para validar los adaptadores nativos.

## Compilar y probar

```bash
dotnet restore Velvet.Launcher.sln
dotnet build Velvet.Launcher.sln --configuration Release
dotnet test Velvet.Launcher.sln --configuration Release
dotnet run --project src/Velvet.Launcher.App/Velvet.Launcher.App.csproj
```

La URL de Master se configura mediante `VELVET_MASTER_URL` y debe ser HTTPS. Por defecto se usa `https://dev.thevelvetstudio.co`. El Launcher usa exclusivamente:

```text
POST /api/launcher/enrollment
GET  /api/launcher/station
POST /api/launcher/heartbeat
```

El enrolamiento genera y persiste un `installationId` estable. La credencial devuelta por Master se guarda en Keychain (macOS) o DPAPI (Windows) y se envía únicamente como `Authorization: Bearer ...`; nunca se incluye en el JSON del heartbeat ni en logs. Los errores 401 detienen los heartbeats y requieren nuevo enrolamiento; 403 marca la estación como no autorizada sin borrar automáticamente la credencial.

Para probar contra otro entorno:

```bash
export VELVET_MASTER_URL="https://staging.example.com"
dotnet run --project src/Velvet.Launcher.App/Velvet.Launcher.App.csproj
```

El estado local del Bridge se consulta por Named Pipe `velvet-bridge` en Windows o Unix Domain Socket en macOS. El socket macOS puede cambiarse con `VELVET_BRIDGE_SOCKET`. La instalación de Chrome y OBS se ofrece explícitamente desde el setup mediante Homebrew (macOS) o WinGet (Windows); MeshCentral se descarga desde su instalador/configuración correspondiente.

## Distribución

El Launcher registra el inicio automático solo cuando se ejecuta como aplicación publicada: LaunchAgent en macOS y `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` en Windows. Las ejecuciones con `dotnet run` no registran autostart; se pueden desactivar con `VELVET_DISABLE_AUTOSTART=1`.

El workflow `.github/workflows/release.yml` genera un instalador Windows x64 y un DMG macOS Apple Silicon (`osx-arm64`) al publicar una etiqueta `vX.Y.Z`.

Consulta el [diagnóstico y plan](docs/diagnostico-y-plan.md) para límites, riesgos y funcionalidades pendientes.

Manuales:

- [Despliegue en macOS](docs/DESPLIEGUE_MACOS.md)
- [Despliegue en Windows](docs/DESPLIEGUE_WINDOWS.md)
- [Pruebas](docs/PRUEBAS.md)
