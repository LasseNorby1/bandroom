FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Bandroom.slnx Directory.Build.props ./
COPY src/Bandroom.Domain/Bandroom.Domain.csproj src/Bandroom.Domain/
COPY src/Bandroom.Api/Bandroom.Api.csproj src/Bandroom.Api/
COPY tests/Bandroom.Domain.Tests/Bandroom.Domain.Tests.csproj tests/Bandroom.Domain.Tests/
RUN dotnet restore Bandroom.slnx
COPY . .
RUN dotnet publish src/Bandroom.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bandroom.Api.dll"]
