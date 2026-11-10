# Contributing / Contribuir

## English

This repository contains the API, its tests and the deployment contract. The frontend is a separate Git repository: keep commits, tags, issues and image builds scoped to the appropriate component. Coordinate matching releases when changing shared HTTP contracts.

1. Read `README.md`, `SECURITY.md` and the relevant architecture/data guide.
2. Use the pinned .NET 10 SDK and locked dependencies. Run `bash scripts/check.sh` or `pwsh -File scripts/check.ps1`.
3. Use synthetic temporary databases and HTTP doubles. Never use a contributor's real Garmin key, health archive, protection store or production volume in tests.
4. Preserve data through reviewed additive EF migrations. Test upgrades from the previous schema. Do not repair startup by deleting a database or down-migrating valuable records.
5. Add real HTTP authorization tests for unauthenticated, member/owner and user-A/user-B cases. Do not substitute hidden frontend controls for server authorization.
6. Adventure packs are manually authored source content. Follow `docs/AUTHORING_ADVENTURES.md`; verify place facts and photo rights, keep stable IDs/goals and historical assets. Never add runtime content generation or assume removable demo files are required.
7. Update both READMEs and version/release notes when behavior/configuration changes. Retain all original third-party licenses; a new dependency requires explicit license/security review.

Pull requests run API verification without registry access. Pushes to `main`/`master` reuse those checks and publish scanned GHCR images; normal commits use branch/commit tags, while a matching `v<VERSION>` Git tag creates a numbered release. Keep application credentials out of Actions; publication uses only `GITHUB_TOKEN`.

Keep changes focused. Explain reproduction, acceptance criteria, tests, migration impact and any checks you could not run. Do not attach private exports, logs with credentials, keys, `.env`, databases or screenshots containing real health records. Report vulnerabilities privately as described in `SECURITY.md`.

Contributions to original project code are submitted under MIT; no additional rights over dependencies, photographs or Garmin technology are implied. Be respectful and constructive. No CLA or private email address is invented by this repository.

## Español

Este repositorio contiene API, pruebas y contrato de despliegue. El frontend tiene otro Git: separa commits, tags, incidencias e imágenes y coordina las versiones cuando cambien contratos HTTP.

Lee README/seguridad y la guía de datos; usa .NET 10 y dependencias bloqueadas; ejecuta el script de comprobación. Las pruebas deben usar bases temporales y respuestas sintéticas, nunca tu Garmin ni datos/volúmenes reales. Conserva el historial mediante migraciones revisadas y pruebas de actualización; no borres bases para arreglar el arranque.

Comprueba autorización real por HTTP entre cuentas, autoría/fuentes/licencias de aventuras y funcionamiento sin ejemplos. Actualiza README en ambos idiomas, versión y notas cuando corresponda. Toda dependencia nueva requiere revisar licencia y seguridad. Describe reproducción, criterios de aceptación, pruebas, migraciones y bloqueos sin adjuntar información privada. Las vulnerabilidades se comunican en privado; las contribuciones de código propio se reciben bajo MIT, sin relicenciar a terceros. Mantén un trato respetuoso y constructivo.

Las pull requests verifican sin acceso al registro. Los pushes a `main`/`master` reutilizan las comprobaciones y publican imágenes analizadas en GHCR; los commits normales usan tags de rama/commit y un tag Git `v<VERSION>` coincidente crea la versión numerada. No añadas credenciales de la app a Actions; solo se utiliza `GITHUB_TOKEN` para publicar.
