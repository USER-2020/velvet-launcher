# Despliegue de VELVET Launcher en Windows

## Alcance

Este manual cubre la publicación para Windows 10/11 (win-x64). El Launcher debe conectarse a Velvet Master mediante HTTPS y no debe usar endpoints de VELVET Agent.

## Requisitos

- Windows 10 u 11 compatible con .NET 10.
- Google Chrome instalado.
- OBS Studio instalado.
- Acceso HTTPS a Velvet Master.
- Permisos para generar un código temporal en Station Manager.
- VELVET Bridge instalado si se necesita reportar el estado de OBS.

## Publicar desde el repositorio

Desde la raíz de velvet-launcher:

~~~powershell
dotnet restore Velvet.Launcher.sln
dotnet publish src/Velvet.Launcher.App/Velvet.Launcher.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output .\artifacts\windows-x64
~~~

Para publicar incluyendo el runtime de .NET:

~~~powershell
dotnet publish src/Velvet.Launcher.App/Velvet.Launcher.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\artifacts\windows-x64-self-contained
~~~

El proyecto todavía no genera un instalador MSI/MSIX. El artefacto publicado debe considerarse una distribución técnica de MVP.

## Configurar Velvet Master

El .env de app_velvet configura Master y no es leído por el Launcher. En cada equipo Windows debe configurarse:

~~~powershell
setx VELVET_MASTER_URL "https://app.thevelvetstudio.co"
~~~

Cierra todas las instancias del Launcher y abre una nueva. Para una prueba solo en la sesión actual:

~~~powershell
$env:VELVET_MASTER_URL = "https://staging.example.com"
~~~

El valor debe ser la URL base, sin /api:

~~~text
https://app.thevelvetstudio.co
~~~

## Configurar VELVET Bridge

El Bridge puede distribuirse como ejecutable:

~~~text
C:\Program Files\VELVET Bridge\Velvet.Bridge.exe
~~~

Configurar su detección:

~~~powershell
[Environment]::SetEnvironmentVariable(
  "VELVET_BRIDGE_PATH",
  "C:\Program Files\VELVET Bridge\Velvet.Bridge.exe",
  "User"
)
~~~

El Bridge debe escuchar únicamente mediante Named Pipe local:

~~~text
velvet-bridge
~~~

No abrir el puerto de OBS WebSocket en el firewall ni publicarlo a Internet.

## Enrolar la estación

1. Abrir Velvet Master.
2. Entrar a /admin/station-manager.
3. Seleccionar el estudio.
4. Generar un código temporal.
5. Introducir el código en la pantalla de enrolamiento del Launcher cuando esté disponible.
6. Confirmar que Master devuelva deviceId, deviceCredential e installationId.

En Windows, la credencial debe guardarse con DPAPI o Windows Credential Manager. No guardarla en JSON, .env, registro no protegido ni logs.

## Verificaciones posteriores

~~~powershell
Get-ChildItem "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe"
Get-ChildItem "${env:ProgramFiles}\obs-studio\bin\64bit\obs64.exe"
echo $env:VELVET_MASTER_URL
~~~

El log se escribe normalmente en:

~~~text
%LOCALAPPDATA%\VelvetLauncher\launcher.log
~~~

## Inicio con Windows

El inicio automático configurable y el instalador oficial todavía no están implementados. No registrar el ejecutable como servicio administrativo hasta definir el modelo de actualización, firma y permisos.

## Limitaciones actuales

- La UI de enrolamiento aún debe conectarse al servicio de enrolamiento.
- El ciclo periódico de heartbeat todavía no está conectado al ciclo de vida de la aplicación.
- La integración funcional con OBS depende de que exista un Bridge compatible.
- El artefacto publicado no reemplaza un instalador firmado.
