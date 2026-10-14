# Image de Lettra.Api (sert aussi l'application WASM). Construite et signée par la CI.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Hosts/Lettra.Api/Lettra.Api.csproj -c Release -o /app /p:CI=true

# Image d'exécution minimale (chiseled) : sans shell ni gestionnaire de paquets, utilisateur non root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Lettra.Api.dll"]
