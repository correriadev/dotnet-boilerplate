# Stage 1: Build & Test
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /app

COPY Pix.HealthCheck.sln ./
COPY src/Pix.HealthCheck.Api/Pix.HealthCheck.Api.csproj src/Pix.HealthCheck.Api/
COPY tests/Pix.HealthCheck.Api.Tests/Pix.HealthCheck.Api.Tests.csproj tests/Pix.HealthCheck.Api.Tests/
RUN dotnet restore

COPY . .
RUN dotnet build -c Release --no-restore
RUN dotnet test -c Release --no-build

RUN dotnet publish src/Pix.HealthCheck.Api/Pix.HealthCheck.Api.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Pix.HealthCheck.Api.dll"]

