# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Assist_IA_Borb.Proxy/Assist_IA_Borb.Proxy.csproj Assist_IA_Borb.Proxy/
RUN dotnet restore Assist_IA_Borb.Proxy/Assist_IA_Borb.Proxy.csproj

COPY Assist_IA_Borb.Proxy/ Assist_IA_Borb.Proxy/
WORKDIR /src/Assist_IA_Borb.Proxy
RUN dotnet publish -c Release -o /app --no-restore

# Runtime (imagem menor, só o necessário pra rodar)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# A porta é definida pela variável de ambiente PORT em plataformas como Render/Railway;
# localmente ou no Azure, cai no padrão do Kestrel (8080).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Assist_IA_Borb.Proxy.dll"]
