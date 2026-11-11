# Security policy / Política de seguridad

## Scope

The 1.0 line is the current maintenance target. This is a private self-hosted application, not a medical platform or a public multi-tenant service. The operator owns host security, HTTPS, proxy configuration, account provisioning, volume permissions and protected backups. No uptime or response-time SLA is promised.

## Report privately

Use the repository's **GitHub private vulnerability reporting** facility when its owner has enabled it. If unavailable, contact the repository owner privately through a channel they publish; do not open a public exploit report or invent a contact email. The owner must configure this reporting channel before inviting public reports.

Include affected versions, deployment mode, the security boundary, a minimal synthetic reproduction and expected impact. Do **not** send passwords, Garmin keys, cookies, integration tokens, protection keys, databases, raw health records or private backup archives. Agree on disclosure before publishing exploit details.

## Operator requirements

- HTTPS is the remote-login default and is required for Internet access. The explicit `Hosting:AllowLanHttp` exception accepts only literal private-IP origins and private/loopback clients; it is unencrypted and must stay on a trusted LAN. Trust only explicit proxy peers or a narrow dedicated private app CIDR; never enable wildcard CORS or trust all forwarded headers to bypass a configuration failure.
- Persist database **and** security state with private ownership/permissions. Back up before upgrades and rehearse a restore in a separate location.
- No default account/password is provided. First-owner bootstrap and owner recovery run in a private console with the API stopped; never pass passwords in argv/environment.
- Use immutable, maintained image versions. Review dependency/base-image advisories and the separate FIT SDK redistribution terms before publication.
- Leave real Garmin imports and periodic schedules disabled until the user configures/opts in. A page read must not contact Garmin.

Deployment requirements and recovery instructions are in `docs/CASAOS.md`. Local audit reports and test evidence are not published as permanent security certifications.

## Español

La línea 1.0 es el objetivo de mantenimiento actual. El propietario administra servidor, HTTPS, proxy, cuentas, permisos y copias; no se promete SLA médico ni de disponibilidad.

Utiliza los informes privados de vulnerabilidad de GitHub si están habilitados; si no, contacta privadamente con el propietario por un canal que publique. No abras una incidencia pública con explotación ni inventes correos. Indica versión, modo de instalación y reproducción sintética, sin claves, cookies, bases, salud ni copias privadas. Acuerda la divulgación.

HTTPS es el modo remoto predeterminado y es obligatorio para Internet. La excepción expresa `Hosting:AllowLanHttp` solo admite un origen con IP privada y clientes privados/loopback; no cifra y debe limitarse a una LAN de confianza. Usa proxies exactos o la subred privada estrecha y exclusiva de la app, conserva ambos volúmenes con permisos privados, prueba recuperación y usa imágenes mantenidas/inmutables. No hay contraseñas predeterminadas; el alta/recuperación del propietario se hace en consola privada con la API parada. No actives Garmin ni horarios por otra persona. Consulta la guía de CasaOS y las condiciones de terceros.
