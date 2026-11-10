# Third-party notices

TrailQuest's original project code is licensed under MIT; see `LICENSE`. Dependencies, maps, photographs, fonts and trademarks retain their own terms. MIT does not relicense Garmin's technology or other third-party material.

The API build retains full package license/notice texts under `/app/licenses`, with a publish-manifest inventory, text hashes and a CycloneDX application SBOM. The web repository generates a separate frontend license bundle. Base framework/OS notices remain in the upstream runtime images; the application SBOM is not a complete OS inventory.

## Route and map data

- Tokyo route geometry and routing data are derived from **OpenStreetMap contributors**, available under the **Open Database License (ODbL) 1.0**: https://www.openstreetmap.org/copyright and https://opendatacommons.org/licenses/odbl/1-0/ . The geometry in `TrailQuest.Api/Content/Adventures/tokyo/adventure.json` retains that data license. Original descriptive text is distinct from map geometry.
- The illustrative Tokyo itinerary was routed using the **FOSSGIS public OSRM walking service** on 2026-11-09: https://routing.openstreetmap.de/ . Routing is not called during normal app use. Do not treat the supplied virtual itinerary as live navigation or safety advice.
- Raster background tiles are fetched directly from `https://tile.openstreetmap.org`. Visible attribution remains on the map. Usage policy: https://operations.osmfoundation.org/policies/tiles/ . No bulk or offline tile downloads are implemented.
- Brumavalle's fictional setting, text and SVG illustrations are original to this project, not third-party fantasy franchise assets.

## Fonts and UI dependencies

- Bricolage Grotesque, DM Sans and IBM Plex Mono are self-hosted through Fontsource packages; their upstream font licenses are **SIL Open Font License 1.1**. The installed packages retain their license files. https://fontsource.org/ .
- Leaflet is BSD-2-Clause. React Leaflet and its core package use **Hippocratic-2.1**, including additional conditions; they are not MIT-only. The frontend retains the complete license, including older MIT material. https://leafletjs.com/ and https://react-leaflet.js.org/ .
- Lucide retains **ISC plus the included Feather-derived MIT notices**, not just an ISC label. https://lucide.dev/license .
- React, Redux Toolkit, React Redux, React Router and other npm dependencies retain the license files supplied in their packages. Exact resolved versions are recorded in `TrailQuest.Front/package-lock.json`.
- ASP.NET Core and EF Core retain their upstream .NET licensing and third-party notices. https://github.com/dotnet/aspnetcore and https://github.com/dotnet/efcore .

## Locally stored photography

- The 1.1 editions add **14 verified Wikimedia Commons photographs** (10 Tokyo stages, 4 additional Hokkaidō stages), alongside the existing Sapporo photograph. Exact filenames, authors, description-page URLs and CC BY / CC BY-SA / CC0 licenses are recorded in each pack's `SOURCES.md` and media metadata. Photo credits and license links are rendered in the place sheet. Source photographs retain those licenses; they are not relicensed as application source code. Local 1280px derivatives are retained; interface crops only adjust display. Incidental urban advertising is not offered as standalone licensed franchise artwork.

- `TrailQuest.Api/Content/Adventures/hokkaido/assets/odori.jpg`: **lumoplank**, “Sapporo - Sapporo7175.jpg”, **CC0 1.0**. Source: https://commons.wikimedia.org/wiki/File:Sapporo_-_Sapporo7175.jpg . License: https://creativecommons.org/publicdomain/zero/1.0/ . Locally stored 1280px derivative; credit is also shown in the gallery and pack `SOURCES.md`. No runtime image hotlinking.

## Garmin FIT SDK

- `Garmin.FIT.Sdk` **21.221.0** decodes recorded FIT data under Garmin's **Flexible and Interoperable Data Transfer (FIT) Protocol License**, not MIT: https://www.nuget.org/packages/Garmin.FIT.Sdk/21.221.0/License . The complete agreement is retained unchanged in the API license bundle. Its internal-use and third-party-distribution conditions apply independently of TrailQuest's code license; retaining a notice is not independent authorization from Garmin to redistribute the SDK.

## Garmin

The integration follows the official Garmin Connect Personal API documentation and protocol. Garmin's Python client was consulted as a protocol reference, not bundled or installed. Garmin names remain their owners' trademarks. This project is not affiliated with or endorsed by Garmin. Use of the Personal API is governed by Garmin's terms and is for the account holder's own data.
