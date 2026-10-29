# Pruebas de VELVET Launcher

## 1. Pruebas automatizadas del Launcher

Desde la raíz del repositorio:

~~~bash
dotnet restore Velvet.Launcher.sln
dotnet build Velvet.Launcher.sln --configuration Release
dotnet test Velvet.Launcher.sln --configuration Release
~~~

Las pruebas cubren actualmente:

- installationId estable.
- Enrolamiento y almacenamiento de credencial.
- Header Bearer.
- Ausencia de credencial dentro del heartbeat.
- Respuesta 204 No Content.
- Respuestas 401 y 403.
- Reintentos para 429.

## 2. Pruebas automatizadas de Master

Desde el repositorio hermano:

~~~bash
cd ../app_velvet
php artisan test tests/Feature/LauncherIntegrationTest.php
php artisan test tests/Feature/StationManagerFoundationTest.php
~~~

La prueba de integración valida:

~~~text
generar token → enrolar → consultar estación → enviar heartbeat 204
~~~

## 3. Prueba local del Master

Preparar dependencias:

~~~bash
cd ../app_velvet
composer install
npm ci
php artisan optimize:clear
php artisan migrate
npm run build
~~~

Para abrir la interfaz:

~~~bash
php artisan serve --host=127.0.0.1 --port=8000
~~~

Esta orden sirve HTTP. Como el contrato Launcher exige HTTPS, para conectar realmente el Launcher se necesita Laravel Herd, Valet o un proxy HTTPS local. Cloudflared también sirve para una prueba temporal.

## 4. Prueba con HTTPS mediante Cloudflared

En una terminal:

~~~bash
cd ../app_velvet
php artisan serve --host=127.0.0.1 --port=8000
~~~

En otra:

~~~bash
cloudflared tunnel --url http://127.0.0.1:8000
~~~

Configura en Master el dominio HTTPS entregado por Cloudflared y los proxies confiables según el entorno. Después, en el Launcher:

~~~bash
export VELVET_MASTER_URL="https://xxxxx.trycloudflare.com"
dotnet run --project src/Velvet.Launcher.App/Velvet.Launcher.App.csproj
~~~

Cloudflared es apropiado para pruebas remotas, no como despliegue permanente de rooms.

## 5. Prueba manual de Station Manager

1. Iniciar sesión en Master.
2. Abrir /admin/station-manager.
3. Confirmar que exista un estudio configurado en LAUNCHER_STUDIO_IDS.
4. Generar un código de enrolamiento.
5. Confirmar que el código tenga vencimiento y sea de un solo uso.
6. Enrolar una estación piloto.
7. Verificar que aparezca en Station Manager.
8. Confirmar installationId, deviceId, room y assignment.

## 6. Verificar HTTP manualmente

No incluir credenciales reales en el shell history. Para una prueba controlada, usar un código temporal recién generado.

~~~bash
curl --fail-with-body --request POST "$VELVET_MASTER_URL/api/launcher/enrollment" \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json' \
  --data '{
    "code": "CODIGO_TEMPORAL",
    "computerName": "MAC-SALA-PARIS",
    "operatingSystem": "macOS",
    "launcherVersion": "0.1.0",
    "installationId": "00000000-0000-0000-0000-000000000001"
  }'
~~~

Consultar estación:

~~~bash
curl --fail-with-body "$VELVET_MASTER_URL/api/launcher/station" \
  --header "Authorization: Bearer $VELVET_DEVICE_CREDENTIAL" \
  --header 'Accept: application/json'
~~~

Heartbeat:

~~~bash
curl --include --request POST "$VELVET_MASTER_URL/api/launcher/heartbeat" \
  --header "Authorization: Bearer $VELVET_DEVICE_CREDENTIAL" \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json' \
  --data '{
    "installationId": "00000000-0000-0000-0000-000000000001",
    "computerName": "MAC-SALA-PARIS",
    "operatingSystem": "macOS",
    "launcherVersion": "0.1.0",
    "sentAt": "2026-10-29T20:00:00Z",
    "status": {
      "bridge": {"connected": false},
      "obs": {"connected": false, "streaming": false, "currentScene": null}
    }
  }'
~~~

El resultado esperado del heartbeat es 204 No Content.

## 7. Prueba del Bridge

Sin Bridge, el heartbeat debe reportar:

~~~json
{
  "bridge": {"connected": false},
  "obs": {"connected": false, "streaming": false, "currentScene": null}
}
~~~

Con Bridge, verificar que:

- macOS escuche en el Unix Domain Socket configurado.
- Windows escuche en el Named Pipe velvet-bridge.
- OBS esté autenticado localmente.
- No exista un puerto público de OBS WebSocket.
- Los logs no contengan la contraseña de OBS ni deviceCredential.

## 8. Respuestas de error esperadas

| Respuesta | Comportamiento esperado |
|---|---|
| 401 | Marcar no autenticada y detener heartbeats; solicitar enrolamiento nuevo. |
| 403 | Mostrar no autorizada o revocada; no borrar automáticamente la credencial. |
| 408/429/5xx | Reintentar con backoff. |
| 204 | Marcar heartbeat como exitoso. |
| Error TLS | No enviar credenciales y mostrar error de conexión segura. |

## 9. Criterio de aprobación para una room piloto

Una room se considera validada cuando:

1. El equipo tiene Chrome y OBS detectados.
2. Master responde por HTTPS.
3. El código temporal enrola una sola vez la estación.
4. La credencial queda almacenada en el almacén seguro del sistema.
5. GET /api/launcher/station responde con la estación correcta.
6. POST /api/launcher/heartbeat responde 204.
7. Station Manager muestra la estación y su última actividad.
8. Un 401 y un 403 producen el comportamiento esperado.
9. No aparecen secretos en los logs.

## Estado actual

El código de transporte, enrolamiento, almacenamiento seguro y heartbeat está implementado y probado. La interfaz visual de enrolamiento y el scheduler periódico de heartbeat todavía deben conectarse al ciclo de vida de la aplicación antes de declarar el despliegue masivo en todas las rooms.
