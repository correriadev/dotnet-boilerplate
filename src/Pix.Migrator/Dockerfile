FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine@sha256:7bc0dd7bcda74a036bfba0eec26f1f516a5ee5b3ea76bda02b405f6eec733e83 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:4a0c84918e9d3d3761a5b6f36eb6db4122d26f6eb80a42ca4288085b3b185ec2 AS build
WORKDIR /src
COPY ["src/Pix.Migrator/Pix.Migrator.csproj", "src/Pix.Migrator/"]
COPY ["src/Pix.Processing.Domain/Pix.Processing.Domain.csproj", "src/Pix.Processing.Domain/"]
COPY ["src/Pix.Processing.Infrastructure/Pix.Processing.Infrastructure.csproj", "src/Pix.Processing.Infrastructure/"]
RUN dotnet restore "src/Pix.Migrator/Pix.Migrator.csproj"
COPY . .
WORKDIR "/src/src/Pix.Migrator"
RUN dotnet build "Pix.Migrator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Pix.Migrator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Pix.Migrator.dll"]
