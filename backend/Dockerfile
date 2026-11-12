FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY QueDrink.Api.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish QueDrink.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
RUN apk add --no-cache curl icu-libs && mkdir /keys && chown $APP_UID /keys
ENV ASPNETCORE_HTTP_PORTS=8080 DATA_PROTECTION_PATH=/keys DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
COPY --from=build /out ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "QueDrink.Api.dll"]
