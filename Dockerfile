FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json nuget.config ./
COPY src/Bandroom.Domain/Bandroom.Domain.csproj src/Bandroom.Domain/
COPY src/Bandroom.Api/Bandroom.Api.csproj src/Bandroom.Api/
RUN dotnet restore src/Bandroom.Api/Bandroom.Api.csproj
COPY src/ src/
RUN dotnet publish src/Bandroom.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl: the container healthcheck and deploy.sh's readiness probe.
# libgssapi-krb5-2: npgsql loads it on connect; the aspnet image (debian,
# arm64 at least) ships without it and the process aborts with
# "Cannot load library libgssapi_krb5.so.2" before serilog can say anything.
RUN apt-get update && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=5 \
  CMD curl -fsS http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Bandroom.Api.dll"]
