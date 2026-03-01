# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution
COPY MineralKingdom/MineralKingdom.sln MineralKingdom/

# Copy csproj files for restore caching
COPY MineralKingdom/MineralKingdom.Api/MineralKingdom.Api.csproj MineralKingdom/MineralKingdom.Api/
COPY MineralKingdom/MineralKingdom.Worker/MineralKingdom.Worker.csproj MineralKingdom/MineralKingdom.Worker/
COPY MineralKingdom/MineralKingdom.Domain/MineralKingdom.Domain.csproj MineralKingdom/MineralKingdom.Domain/
COPY MineralKingdom/MineralKingdom.Infrastructure/MineralKingdom.Infrastructure.csproj MineralKingdom/MineralKingdom.Infrastructure/
COPY MineralKingdom/MineralKingdom.Contracts/MineralKingdom.Contracts.csproj MineralKingdom/MineralKingdom.Contracts/

# If you have these files, copying them BEFORE restore helps avoid weird restore diffs:
# COPY MineralKingdom/Directory.Build.props MineralKingdom/
# COPY MineralKingdom/NuGet.config MineralKingdom/

RUN dotnet restore MineralKingdom/MineralKingdom.Api/MineralKingdom.Api.csproj
RUN dotnet restore MineralKingdom/MineralKingdom.Worker/MineralKingdom.Worker.csproj

# Copy everything and publish
COPY . ./

RUN dotnet publish MineralKingdom/MineralKingdom.Api/MineralKingdom.Api.csproj -c Release -o /out/api
RUN dotnet publish MineralKingdom/MineralKingdom.Worker/MineralKingdom.Worker.csproj -c Release -o /out/worker

# ---------- runtime: api ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS api
WORKDIR /app
COPY --from=build /out/api ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MineralKingdom.Api.dll"]

# ---------- runtime: worker ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS worker
WORKDIR /app
COPY --from=build /out/worker ./
ENTRYPOINT ["dotnet", "MineralKingdom.Worker.dll"]