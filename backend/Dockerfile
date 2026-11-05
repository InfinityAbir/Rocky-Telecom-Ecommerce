# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY RockyTelecom.Domain/RockyTelecom.Domain.csproj RockyTelecom.Domain/
COPY RockyTelecom.Application/RockyTelecom.Application.csproj RockyTelecom.Application/
COPY RockyTelecom.Infrastructure/RockyTelecom.Infrastructure.csproj RockyTelecom.Infrastructure/
COPY RockyTelecom.Api/RockyTelecom.Api.csproj RockyTelecom.Api/
RUN dotnet restore RockyTelecom.Api/RockyTelecom.Api.csproj
COPY . .
RUN dotnet publish RockyTelecom.Api/RockyTelecom.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Kestrel listens on 8080 inside the container; nginx is the only public entry point.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app .
RUN mkdir -p /app/wwwroot/uploads /app/logs \
 && adduser --disabled-password --gecos "" --uid 10001 rocky \
 && chown -R rocky /app/wwwroot/uploads /app/logs
USER rocky
EXPOSE 8080
ENTRYPOINT ["dotnet", "RockyTelecom.Api.dll"]
