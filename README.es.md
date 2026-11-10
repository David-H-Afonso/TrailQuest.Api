# TrailQuest API

[English](README.md) · [Contribuir](CONTRIBUTING.md) · [Seguridad](SECURITY.md) · [Licencia](LICENSE)

Archivo privado de actividad y aventuras a pie, alojado por ti. Los pasos diarios hacen avanzar un único pasaporte virtual activo por perfil; cada usuario puede guardar salud y actividad de Garmin, importar FIT/GPX/TCX, conseguir sellos y compartir voluntariamente con sus amistades. Salir de un pasaporte conserva su avance y sus sellos. SQLite es la fuente de datos; abrir una pantalla nunca inicia una importación externa.

Este es el **repositorio independiente del backend**. La aplicación React y la imagen `trailquest-web` se mantienen por separado. No se incluyen usuarios ni contraseñas, SSO central ni una cronología pública de salud. La app no es un servicio médico ni de navegación.

## Arranque local

Necesitas el **SDK .NET 10 fijado** en `global.json` o un parche compatible de esa banda. Para Docker no hace falta instalar el SDK en el servidor.

```bash
dotnet restore TrailQuest.Api.sln --locked-mode
dotnet run --project TrailQuest.Api.csproj --no-launch-profile -- --urls http://127.0.0.1:5187
```

Arranca el frontend por separado y abre su dirección loopback. Una instalación local sin cuentas permite crear el propietario en la interfaz. Las cuentas nuevas no tienen actividad personal. Los ejemplos están apagados salvo que los actives expresamente; retirarlos no elimina las aventuras ni el progreso personal.

El almacenamiento predeterminado es `data/`, relativo a la raíz de contenido de la API; las variables de entorno tienen prioridad. No ejecutes varias versiones de la API sobre el mismo directorio SQLite.

## CasaOS / Docker

El despliegue consta de **dos imágenes**, `trailquest-api` y `trailquest-web`, una red privada y dos montajes persistentes de la API. Solo se publica el puerto privado de la web. Tu proxy termina HTTPS; el navegador accede a `/api/v1` desde el mismo origen.

Sigue la [guía de instalación y recuperación de CasaOS](docs/CASAOS.es.md) ([English](docs/CASAOS.md)). La imagen rechaza el alta del primer propietario por acceso remoto. Créalo desde una consola privada, con la instalación detenida, **antes** de levantar el stack:

```bash
docker compose --env-file deploy/.env -f deploy/docker-compose.yml run --rm --no-deps trailquest-api --Account:Action=create-owner --Account:Username=tu-usuario-elegido
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
```

La consola pide la contraseña oculta dos veces. No la pongas en argumentos, variables, Compose ni capas de imagen. Para recuperar un propietario existente, usa `Account:Action=recover-owner`, con la API detenida: revoca las sesiones anteriores y conserva la actividad. Cada miembro puede cambiar su contraseña desde su cuenta; no hay recuperación por correo.

## Configuración

Copia `deploy/.env.example` a un `.env` privado e ignorado junto a Compose y rellena **tu propia** instalación. Contiene ejemplos, no direcciones ni credenciales reales del servidor.

| Nombres | Finalidad |
| --- | --- |
| `TRAILQUEST_API_IMAGE`, `TRAILQUEST_WEB_IMAGE` | Referencias publicadas de API/web; usa tags de versión o digests para una instalación fijada |
| `TRAILQUEST_PUBLIC_HOST`, `TRAILQUEST_PUBLIC_HTTPS_PORT` | Dirección HTTPS externa y enlace de CasaOS |
| `TRAILQUEST_BIND_ADDRESS`, `TRAILQUEST_HTTP_PORT` | Interfaz/puerto privado accesible solo para el proxy HTTPS |
| `TRAILQUEST_TRUSTED_PROXY` | IP exacta del proxy que ve el contenedor web; nunca CIDR/comodín |
| `TRAILQUEST_SUBNET`, `TRAILQUEST_WEB_IP`, `TRAILQUEST_API_IP` | Subred Docker privada libre e IP exacta de cada servicio |
| `TRAILQUEST_DATA_DIR`, `TRAILQUEST_SECURITY_DIR` | Directorios persistentes de la base y protección de la instalación |
| `TRAILQUEST_UID`, `TRAILQUEST_GID` | Propietario de los directorios reales montados en la API |
| `TRAILQUEST_TIME_ZONE`, `TRAILQUEST_DEMO_ENABLED` | Zona del contenedor y activación expresa de los ejemplos |

Las opciones nativas son `Data__Directory`, `Security__KeyDirectory`, `Hosting__AllowRemote`, `Hosting__PublicOrigin`, los `Hosting__KnownProxies__N` numerados, `Hosting__LocalWebPort`, `Hosting__LocalApiPort`, `Demo__Enabled` y `Demo__FixturePath` opcional. Las claves de Garmin se introducen privadamente **en Ajustes de cada cuenta**, no en el contenedor. La importación periódica empieza apagada y requiere la elección expresa del usuario.

## Comprobar y compilar

```bash
bash scripts/check.sh
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project TrailQuest.Api.csproj
docker build --build-arg RELEASE_VERSION=1.0.0 -t trailquest-api:1.0.0 .
```

En Windows: `pwsh -NoLogo -NoProfile -File scripts/check.ps1`. Las pruebas crean bases sintéticas y dobles HTTP; nunca necesitan tu Garmin. Los Dockerfiles fijan los digests de las bases, ejecutan sin root y excluyen configuración local, bases, claves y compilaciones del contexto. Release no incluye ensamblados de ejecución de herramientas EF de diseño. Las licencias y el inventario CycloneDX se generan a partir de los paquetes publicados, no de toda la caché de desarrollo.

## Imágenes Docker automáticas

Cada push a `main` o `master` ejecuta las comprobaciones reutilizables de API, construye la imagen Linux amd64, analiza vulnerabilidades/secretos y publica en **GHCR** solo si todo pasa. Las pull requests verifican sin publicar. Se mantiene `workflow_dispatch` para las ramas admitidas y tags de versión; no hay ejecuciones programadas ni despliegue automático en CasaOS.

| Origen | Tags publicados |
| --- | --- |
| Push a la rama predeterminada | `latest`, el nombre de rama y `sha-<commit-completo>` |
| Otra rama admitida | El nombre de rama y `sha-<commit-completo>`; no sustituye `latest` |
| Tag `v1.0.0` coincidente con `VERSION` | `1.0.0`, `v1.0.0` y `sha-<commit-completo>`; se rechazan versiones ya publicadas |

Los commits normales no necesitan cambiar la versión ni crear un tag. Tras la primera ejecución correcta que publique ambos paquetes:

```bash
docker pull ghcr.io/david-h-afonso/trailquest-api:latest
docker pull ghcr.io/david-h-afonso/trailquest-web:latest
```

La publicación utiliza `GITHUB_TOKEN` limitado al job con `packages: write`, no un token personal ni credenciales de la app. Los forks publican bajo su propio propietario en minúsculas. El resumen de Actions incluye tags, SHA, digest y el comando de descarga fijada; utiliza ese digest para instalaciones reproducibles. Para descargar desde CasaOS sin iniciar sesión, haz públicos los paquetes GHCR tras su primera publicación; si no, autentica Docker en el servidor. Consulta [la guía de CasaOS](docs/CASAOS.es.md).

Revisa los términos de dependencias/fotos, incluido Garmin FIT, antes de distribuir: automatizar no concede derechos de redistribución. La visibilidad, protección de ramas y límites de Actions siguen siendo ajustes del propietario. Los repos privados pueden consumir minutos facturables.

## Datos y límites

- Conserva **`/data` y `/security`**. Perder el segundo puede invalidar sesiones y requerir reconectar Garmin. No se trasladan silenciosamente a Linux secretos protegidos por DPAPI de Windows.
- Las migraciones conservan pasos, fechas, historial y sellos. La cuenta de puntos GPS se deriva del JSON sin reemplazarlo. La migración de pasaporte único conserva activo el más antiguo de cada perfil y guarda los demás como inactivos, ordenando por fecha de inicio, creación e ID. Haz copia antes de actualizar y no ejecutes migraciones destructivas hacia atrás sobre actividad valiosa.
- El JSON no sustituye una copia recuperable completa ni incluye una restauración JSON implementada. Excluye originales y credenciales. Permite exportación por fechas, una exportación simultánea por cuenta y límites explícitos; una selección demasiado grande falla sin truncar registros.
- Garmin importa hasta 28 días inclusivos por lote; las consultas del archivo admiten 366. Los archivos tienen un límite de 32 MiB; ZIP no se extrae a disco, XML desactiva entidades externas y FIT comprueba integridad.
- SQLite no cifra el archivo entero. Protege el servidor, volúmenes y copias. Nunca publiques los directorios de datos o protección como contenido web.

Detalles: [datos e integraciones](docs/DATA_AND_INTEGRATIONS.md), [autoría de aventuras](docs/AUTHORING_ADVENTURES.md), [notas de versión](RELEASE_NOTES.md).

## Licencia y ayuda

Código propio: **MIT**. Fotografías, geometría, dependencias y **SDK FIT de Garmin** conservan sus condiciones: [avisos de terceros](THIRD_PARTY_NOTICES.md). Conservar el contrato del SDK no implica autorización general para redistribuirlo; revisa sus condiciones antes de publicar binarios o contenedores. No hay afiliación oficial con Garmin, proveedores de mapas ni franquicias.

Para incidencias normales, indica versión, modo de instalación y reproducción sin información privada. Para vulnerabilidades usa [SECURITY.md](SECURITY.md). Si no arranca, comprueba propietario de carpetas, HTTPS/proxy exactos, los dos volúmenes y `/health/ready`. No pegues credenciales ni exportaciones de salud en issues.
