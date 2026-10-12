FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Core/Core.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# HTTP_PORTS y no URLS: la imagen aspnet ya define HTTP_PORTS=8080 y ambas juntas provocan el aviso
# «Overriding HTTP_PORTS» (Hosting.Diagnostics[15]) en cada arranque.
ENV ASPNETCORE_HTTP_PORTS=8080
# Sin privilegios: APP_UID viene definido en las imágenes aspnet (usuario "app").
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "MiParte.Core.Api.dll"]
