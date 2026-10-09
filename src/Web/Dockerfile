FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Web -c Release -o /app

# Sin privilegios (usuario nginx, puerto 8080). La plantilla se procesa al arrancar con envsubst.
FROM nginxinc/nginx-unprivileged:alpine
# Orígenes a los que la CSP permite conectar (connect-src). Deben coincidir con Supabase:Url y Api:CoreUrl del
# appsettings.json del front (ASSISTANT_URL es opcional: sin él el chat queda desactivado); vacíos, el front no podrá hablar con ninguno.
ENV SUPABASE_URL="" CORE_URL="" ASSISTANT_URL=""
COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY src/Web/default.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 8080
