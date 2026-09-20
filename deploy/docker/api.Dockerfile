FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

COPY Directory.Build.props global.json ./
COPY src/TaskPulse.Api/TaskPulse.Api.csproj src/TaskPulse.Api/
RUN dotnet restore src/TaskPulse.Api/TaskPulse.Api.csproj

COPY src/TaskPulse.Api/ src/TaskPulse.Api/
RUN dotnet publish src/TaskPulse.Api/TaskPulse.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data/uploads && chown -R app:app /data
VOLUME /data

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TaskPulse.Api.dll"]
