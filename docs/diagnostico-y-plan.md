# Diagnóstico y alcance del MVP

## Auditoría

El repositorio inicial no tenía archivos de solución, proyectos, commits ni ramas con contenido. El remoto configurado (`https://github.com/USER-2020/velvet-launcher.git`) es un placeholder y no pudo consultarse por falta de resolución de red. No existen VELVET Master, VELVET Agent, contratos HTTP, tablas, integración OBS ni Bridge reutilizables en este checkout.

## Arquitectura propuesta

- `Velvet.Launcher.Core`: contratos, modelos y límites de plataforma.
- `Velvet.Launcher.Application`: casos de uso de estado y enrolamiento.
- `Velvet.Launcher.Infrastructure`: detección local, HTTP con Master, logging y almacenamiento seguro.
- `Velvet.Launcher.App`: shell Avalonia multiplataforma y pantalla inicial.
- `tests/Velvet.Launcher.UnitTests`: pruebas del comportamiento de aplicación con dobles de prueba.

La URL de Master es configurable mediante `VELVET_MASTER_URL` en el punto de composición de la UI y debe ser HTTPS. Los endpoints usados son únicamente `/api/launcher/enrollment`, `/api/launcher/heartbeat` y `/api/launcher/station`. El repositorio hermano `../app_velvet` fue inspeccionado y contiene `LauncherController`, `AuthenticateLauncher`, `LauncherTransport`, migraciones y pruebas de integración para este contrato.

## Componentes reutilizados y nuevos

No fue posible reutilizar componentes existentes. Todo lo incluido es nuevo y se mantiene detrás de interfaces para permitir conectar contratos reales de Master y adaptadores de Windows/macOS posteriormente.

## Persistencia y seguridad

No se crean migraciones de base de datos porque el backend no está presente. La credencial de dispositivo se almacena mediante macOS Keychain o DPAPI en Windows; nunca se escribe en texto plano. El código de enrolamiento se envía únicamente por HTTPS y el Launcher no maneja contraseñas de modelos.

## Estado por etapas

Implementado en este MVP: etapas 1, 2 y 3 a nivel de cliente, con UI inicial, detección local, `installationId` estable, enrolamiento, Bearer en header, heartbeat con payload de Bridge/OBS, tratamiento de 401/403, reintentos 408/429/5xx, logs sin credenciales, estado local del Bridge y pruebas unitarias.

Pendiente y deliberadamente fuera de este cambio: preparación automática de estación (etapa 4), Bridge/OBS WebSocket (etapa 5), panel de Master (etapa 6) e instaladores/actualizaciones (etapa 7). No se declara soporte macOS validado hasta compilar y ejecutar en un equipo macOS real.

## Riesgos

1. Los contratos de Master son propuestas y deben alinearse con el backend real.
2. Las rutas de Chrome/OBS varían por instalación, arquitectura y permisos.
3. La instalación automática requiere un proveedor firmado, validación de integridad y consentimiento explícito.
4. La coordinación con VELVET Agent y el control de OBS todavía no están implementados.
5. Avalonia y el runner de pruebas requieren restaurar paquetes NuGet en un entorno con acceso al feed.

## Plan siguiente

1. Recibir o conectar el repositorio real de Master/Agent y confirmar contratos.
2. Añadir pruebas de contrato contra un entorno de staging.
3. Completar el adaptador Windows Credential Manager si la política exige Credential Manager en lugar de DPAPI.
4. Tras revisión del MVP, abordar etapas 4 y 5 de forma separada.
