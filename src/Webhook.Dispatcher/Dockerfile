FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine@sha256:7bc0dd7bcda74a036bfba0eec26f1f516a5ee5b3ea76bda02b405f6eec733e83 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:4a0c84918e9d3d3761a5b6f36eb6db4122d26f6eb80a42ca4288085b3b185ec2 AS build
WORKDIR /src
COPY ["src/Webhook.Dispatcher/Webhook.Dispatcher.csproj", "src/Webhook.Dispatcher/"]
COPY ["src/Pix.Callbacks.Domain/Pix.Callbacks.Domain.csproj", "src/Pix.Callbacks.Domain/"]
COPY ["src/Pix.Callbacks.Application/Pix.Callbacks.Application.csproj", "src/Pix.Callbacks.Application/"]
COPY ["src/Pix.Callbacks.Infrastructure/Pix.Callbacks.Infrastructure.csproj", "src/Pix.Callbacks.Infrastructure/"]
RUN dotnet restore "src/Webhook.Dispatcher/Webhook.Dispatcher.csproj"
COPY . .
WORKDIR "/src/src/Webhook.Dispatcher"
RUN dotnet build "Webhook.Dispatcher.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Webhook.Dispatcher.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Webhook.Dispatcher.dll"]
