# CasaOS: instalación, actualización y recuperación

[English](CASAOS.md)

## Arquitectura y requisitos

Usa **`deploy/docker-compose.yml` como proyecto canónico**, con las imágenes API/web publicadas por separado para la misma versión. No hacen falta `docker run`, contenedores de producción alternativos ni otro stack paralelo. Esta guía no despliega automáticamente.

La API escucha en 8080 sin publicar puerto al host. Nginx Web sirve interfaz y `/api` desde el mismo origen. Tu proxy HTTPS accede al puerto privado de Web. Web solo confía en su proxy inmediato y API en la IP exacta de Web. La subred Docker no debe solaparse con otra; Garmin necesita salida a internet solo para importaciones solicitadas, no para consultar lo guardado.

Requisitos: Linux amd64, Docker/Compose v2, dominio/certificado HTTPS y proxy administrado por ti. La candidata se comprueba en Docker local aislado; DNS, TLS y firewall reales de CasaOS son verificaciones humanas. No se anuncia ARM64 solo porque existan imágenes base para él.

## Imágenes publicadas

Cada repositorio verifica, construye, analiza y publica su imagen GHCR al hacer push a `main`/`master`. Cuando ambos workflows terminen correctamente, los ejemplos usan `ghcr.io/david-h-afonso/trailquest-api:latest` y `ghcr.io/david-h-afonso/trailquest-web:latest`. No necesitas compilar localmente ni una credencial de la app para descargarlas. Los forks publican bajo su propio propietario en minúsculas.

En GitHub **Packages → paquete → Package settings**, selecciona **Public** para descargar desde CasaOS sin iniciar sesión, o autentica Docker si los paquetes son privados. Un repositorio público no garantiza por sí solo el acceso anónimo al paquete. Si la publicación falla por permisos, revisa el permiso de escritura de paquetes de Actions y el acceso del repositorio/Actions al paquete; los workflows usan `GITHUB_TOKEN`, no un token personal guardado.

Fija la pareja API/web probada con `sha-<commit-completo>` o el digest `@sha256:…` que muestra el resumen de Actions. `latest` sigue la rama predeterminada y no es inmutable; las imágenes numeradas aparecen al subir el tag de versión `v<VERSION>` correspondiente. Conserva los digests anteriores y acuerda la copia de datos/protección antes de actualizar. Actions nunca contacta ni despliega en tu servidor CasaOS.

## Configura antes de arrancar

1. Copia `deploy/.env.example` a `deploy/.env`, privado e ignorado. Elige las referencias API/web publicadas, preferiblemente los digests inmutables probados; no trates `latest` como inmutable.
2. Configura host/puerto HTTPS, interfaz/puerto HTTP privado, zona horaria, subred libre e IP de cada servicio. CasaOS abre **la dirección HTTPS pública**, no HTTPS sobre el puerto HTTP interno.
3. Indica la IP exacta del proxy que ve Web: NAT puede hacer que no coincida con la IP del contenedor del proxy. Nunca uses una red/comodín ni confíes en cabeceras de cualquier cliente. Restringe el puerto HTTP privado al proxy mediante red/firewall.
4. Crea los directorios reales para `/data` y `/security`, con propietario UID/GID configurados y permisos privados. Para los valores del ejemplo, un administrador humano puede ejecutar:

   ```bash
   sudo install -d -m 0700 -o 1654 -g 1654 /DATA/AppData/trailquest/data /DATA/AppData/trailquest/security
   ```

   Adapta estas rutas e IDs explícitos. No uses 0777 ni cambies recursivamente directorios ajenos. Compose no crea los montajes ausentes como root.

5. Valida sin volcar configuración:

   ```bash
   docker compose --env-file deploy/.env -f deploy/docker-compose.yml config --quiet
   ```

No añadas contraseñas, claves Garmin ni claves criptográficas. `TRAILQUEST_DEMO_ENABLED` empieza apagado. Garmin se conecta desde Ajustes de cada cuenta y los horarios están desactivados inicialmente.

## Propietario y primer arranque

Mantén la API parada al configurar el propietario. Si CasaOS intentó arrancar una instalación vacía, detén ese servicio; el arranque remoto sin cuentas falla expresamente.

```bash
docker compose --env-file deploy/.env -f deploy/docker-compose.yml run --rm --no-deps trailquest-api --Account:Action=create-owner --Account:Username=tu-usuario-elegido
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
docker compose --env-file deploy/.env -f deploy/docker-compose.yml ps
```

Usa consola interactiva privada: pide contraseña oculta y confirmación. `Account:DisplayName` es opcional; no se proporciona un usuario predeterminado. Repetir el alta rechaza la existencia del propietario sin reiniciar registros. El modo stdin explícito es para gestores de secretos/pruebas, nunca para escribir una contraseña real en el historial del shell o el entorno de la imagen.

Configura el proxy externo para conservar Host e indicar HTTPS, admitir un archivo de 32 MiB más su envoltorio y esperar un lote Garmin acotado. No abras CORS indiscriminadamente. Antes de compartir acceso, verifica HTTPS, cookie segura limitada al host, aislamiento propietario/miembros y una importación breve de Garmin aprobada por el usuario.

Para importar una app personalizada en CasaOS, utiliza el Compose canónico resuelto con tus imágenes y configuración. Conserva los dos servicios en ese proyecto. Los metadatos incluyen dominio/puerto HTTPS, volúmenes y versión, sin publicar en una tienda ni registrar un dominio.

## Actualizar

Detén la API antigua, guarda una copia protegida y las referencias de las imágenes anteriores, descarga ambas imágenes nuevas y recrea el mismo proyecto. La API aplica migraciones hacia delante. La cuenta de puntos GPS se añade sin reemplazar trazados u originales. No ejecutes varias versiones sobre la misma base.

La migración de pasaporte único conserva todos los pasaportes, pasos, fechas de inicio y sellos. Si un perfil tiene varios activos, mantiene el más antiguo, ordenando por fecha de inicio, creación e ID. Los demás quedan inactivos con el avance congelado. Salir o cambiar después conserva los mismos datos; retomar cuenta los nuevos pasos registrados desde hoy, no el periodo inactivo. Los perfiles personal/de prueba y los demás usuarios siguen aislados. Actualiza API y web antes de usar estos controles.

No copies silenciosamente una base personal o claves de desarrollo dentro de la imagen. Un traslado desde Windows se planifica y respalda expresamente: conserva registros, pero la protección DPAPI no se traslada automáticamente a Linux y el usuario deberá reconectar Garmin allí.

## Copia y restauración

**Solo base:** `--Backup:Destination=<ruta-nueva>` crea una copia SQLite coherente, comprueba integridad, rechaza sobrescritura y sale antes de migrar o abrir protección. El destino debe ser privado y escribible. No incluye claves de protección; el JSON tampoco sustituye esta copia ni tiene restauración implementada.

**Recuperación conservando credenciales:** acuerda por separado copiar el `/security` correspondiente, con `installation.id` y el anillo de claves. Detén la API y respalda **ambos directorios completos juntos**, preservando propietario y permisos, con tu método protegido. Un archivo sin cifrar contiene información y material criptográfico sensibles: restringe su acceso, preferiblemente cifra su almacenamiento y nunca lo subas a GitHub o un recurso público. No se instala ningún horario de copias.

Antes de recuperar, conserva el estado nuevo/fallido. Restaura una copia verificada en **directorios privados nuevos**, apunta Compose a ellos, selecciona las imágenes API/web correspondientes y recrea el proyecto canónico. Comprueba sesión, pasos/sellos, propiedad y estado Garmin sin publicar registros. Conserva las carpetas anteriores hasta aprobar la recuperación. No borres tablas, archivos WAL en uso ni uses migraciones destructivas `Down` para volver a una imagen.

La pérdida máxima depende de tu última copia manual protegida; no se promete SLA automático. Ensaya la restauración con datos sintéticos y fija tu objetivo de tiempo de recuperación.

## Recuperar propietario y resolver problemas

Con API parada, usa la consola privada anterior con `--Account:Action=recover-owner` y **el usuario propietario existente**. No convierte miembros en propietarios ni crea una puerta trasera; invalida sus sesiones anteriores. Cada miembro cambia su contraseña; no se anuncia recuperación por correo.

| Síntoma | Comprobar |
| --- | --- |
| Error de montaje/permisos | Existen ambos directorios, pertenecen al UID/GID configurado y admiten permisos privados Linux |
| Otro proceso usa los datos | Detén la otra API o mantenimiento; no borres un bloqueo activo |
| 400/403 por origen/HTTPS | Host/puerto públicos, IP del proxy inmediato y de Web; el log saneado de Web incluye `peer`, no cookies ni cuerpos |
| API no saludable | Prueba interna `/health/ready`, arranque/migraciones, permisos y propietario inicial |
| Archivo 413 | Reduce días/registros o exporta por fechas; no se ha truncado ni borrado información |
| Garmin pide reconectar | Falta protección correspondiente o es incompatible; reconecta privadamente, sin extraer secretos de archivos/logs |
| Falla el mapa | Usa el esquema local; no automatices reintentos o descargas de tiles |

Al informar, conserva versiones/IDs de petición sin credenciales ni salud. Consulta [seguridad](../SECURITY.md), [avisos de terceros](../THIRD_PARTY_NOTICES.md) y [notas de versión](../RELEASE_NOTES.md) antes de exponer la instalación.
