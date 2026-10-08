FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Assistant/Assistant.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
# Sin privilegios: APP_UID viene definido en las imágenes aspnet (usuario "app").
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "MiParte.Assistant.Api.dll"]
