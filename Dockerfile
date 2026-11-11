# syntax=docker/dockerfile:1
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.14-noble@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c
FROM --platform=$BUILDPLATFORM ${SDK_IMAGE} AS build
WORKDIR /src
ARG RELEASE_VERSION=1.0.0
ARG SOURCE_REVISION=unversioned
COPY VERSION Directory.Build.props global.json TrailQuest.Api.csproj packages.lock.json ./
RUN test "$(cat VERSION)" = "$RELEASE_VERSION" && dotnet restore TrailQuest.Api.csproj --locked-mode -p:Configuration=Release
COPY . .
RUN dotnet publish TrailQuest.Api.csproj --no-restore -c Release -o /out -p:UseAppHost=false -p:SourceRevisionId="$SOURCE_REVISION"
RUN dotnet run --project tools/LicenseBundle/LicenseBundle.csproj -c Release -- /out /root/.nuget/packages /src/third_party/licenses "$RELEASE_VERSION"

FROM ${RUNTIME_IMAGE} AS runtime
ARG RELEASE_VERSION=1.0.0
ARG SOURCE_REVISION=unversioned
LABEL org.opencontainers.image.title="TrailQuest API" \
      org.opencontainers.image.description="Project code MIT; dependencies retain their licenses and notices" \
      org.opencontainers.image.version="$RELEASE_VERSION" \
      org.opencontainers.image.revision="$SOURCE_REVISION" \
      org.opencontainers.image.licenses="MIT"
WORKDIR /app
# Pinned security updates for the otherwise immutable Ubuntu base (2026-11-10).
# Build workers may have no IPv6 route; this does not change application networking.
RUN apt-get -o Acquire::ForceIPv4=true -o Acquire::Retries=3 -o APT::Update::Error-Mode=any update \
    && apt-get -o Acquire::ForceIPv4=true -o Acquire::Retries=3 install -y --no-install-recommends --only-upgrade \
      apt=2.8.4ubuntu0.1 libapt-pkg6.0t64=2.8.4ubuntu0.1 libpcre2-8-0=10.42-4ubuntu2.2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/ ./
COPY LICENSE THIRD_PARTY_NOTICES.md ./
COPY LICENSE THIRD_PARTY_NOTICES.md /app/licenses/
RUN mkdir -p /data /security && chown "$APP_UID:$APP_UID" /data /security && chmod 700 /data /security
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Data__Directory=/data \
    Security__KeyDirectory=/security \
    Demo__Enabled=false \
    DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "TrailQuest.Api.dll", "--Health:Probe=true"]
ENTRYPOINT ["dotnet", "TrailQuest.Api.dll"]
