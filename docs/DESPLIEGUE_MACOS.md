# Despliegue de VELVET Launcher en macOS

## Alcance

Este manual cubre la ejecución publicada del Launcher en macOS Apple Silicon (osx-arm64) e Intel (osx-x64). El Launcher debe conectarse a una URL HTTPS de Velvet Master. No se debe usar http:// en una instalación real.

## Requisitos

- macOS compatible con .NET 10.
- Google Chrome instalado en /Applications/Google Chrome.app.
- OBS Studio instalado en /Applications/OBS.app.
- Acceso de red a Velvet Master por HTTPS.
- Credenciales para generar un código temporal en Station Manager.
- VELVET Bridge instalado si se quiere reportar el estado de OBS.

Para verificar el runtime durante desarrollo:

~~~bash
dotnet --info
~~~

## Publicar desde el repositorio

Desde la raíz de velvet-launcher:

~~~bash
dotnet restore Velvet.Launcher.sln
dotnet publish src/Velvet.Launcher.App/Velvet.Launcher.App.csproj \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained false \
  --output ./artifacts/macos-arm64
~~~

Para un Mac Intel:

~~~bash
dotnet publish src/Velvet.Launcher.App/Velvet.Launcher.App.csproj \
  --configuration Release \
  --runtime osx-x64 \
  --self-contained false \
  --output ./artifacts/macos-x64
~~~

--self-contained false requiere el runtime de .NET 10. Para distribuir el runtime dentro del paquete, usar --self-contained true; el artefacto será más grande.

## Configurar Velvet Master

El Launcher no lee el .env de app_velvet. Debe recibir su propia variable:

~~~bash
export VELVET_MASTER_URL="https://app.thevelvetstudio.co"
~~~

El valor debe ser la URL base, sin /api:

~~~text
Correcto: https://app.thevelvetstudio.co
Incorrecto: https://app.thevelvetstudio.co/api
~~~

Para una aplicación abierta desde Finder, registrar la variable en el entorno de aplicaciones gráficas y reiniciar el Launcher:

~~~bash
launchctl setenv VELVET_MASTER_URL "https://app.thevelvetstudio.co"
~~~

Para desarrollo desde Terminal:

~~~bash
export VELVET_MASTER_URL="https://staging.example.com"
./artifacts/macos-arm64/Velvet.Launcher.App
~~~

El proyecto también puede iniciarse sin publicar:

~~~bash
dotnet run --project src/Velvet.Launcher.App/Velvet.Launcher.App.csproj
~~~

## Configurar VELVET Bridge

El Bridge puede instalarse como aplicación:

~~~text
/Applications/VELVET Bridge.app
~~~

El estado local se consulta mediante Unix Domain Socket. La ruta por defecto es:

~~~text
~/Library/Application Support/VelvetLauncher/bridge.sock
~~~

Para usar otra ruta:

~~~bash
export VELVET_BRIDGE_SOCKET="$HOME/Library/Application Support/VelvetLauncher/bridge.sock"
~~~

El Bridge no debe exponer el WebSocket de OBS a Internet.

## Enrolar la estación

1. Abrir Velvet Master.
2. Entrar a /admin/station-manager.
3. Seleccionar el estudio.
4. Generar un código temporal.
5. Introducir el código en la pantalla de enrolamiento del Launcher cuando esté disponible.
6. Confirmar que Master devuelve deviceId, deviceCredential e installationId.

La credencial se almacena en macOS Keychain. Nunca debe copiarse a archivos de configuración, logs ni tickets.

## Verificaciones posteriores

~~~bash
test -d "/Applications/Google Chrome.app" && echo "Chrome disponible"
test -d "/Applications/OBS.app" && echo "OBS disponible"
echo "$VELVET_MASTER_URL"
~~~

El log se escribe normalmente en:

~~~text
~/Library/Application Support/VelvetLauncher/launcher.log
~~~

## Seguridad y distribución

Para distribución fuera de desarrollo se requiere crear un .app firmado y notarizado. Este repositorio todavía no contiene el empaquetado DMG/PKG ni el flujo de firma/notarización.

## Limitaciones actuales

- La UI de enrolamiento aún debe conectarse al servicio de enrolamiento.
- El ciclo periódico de heartbeat todavía no está conectado al ciclo de vida de la aplicación.
- La integración funcional con OBS depende de que exista un Bridge compatible.
- No declarar el soporte macOS como validado hasta probar el artefacto en un Mac físico.
