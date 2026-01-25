# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy solution + restore (adjust if your .sln is elsewhere)
# If the .sln is at mineral-kingdom-api/MineralKingdom/MineralKingdom.sln, this is correct:
COPY MineralKingdom/MineralKingdom.sln MineralKingdom/

# Copy csproj files for restore caching
COPY MineralKingdom/MineralKingdom.Api/MineralKingdom.Api.csproj MineralKingdom/MineralKingdom.Api/
COPY MineralKingdom/MineralKingdom.Worker/MineralKingdom.Worker.csproj MineralKingdom/MineralKingdom.Worker/

# If you already created these libraries, keep these lines; otherwise remove for now.
# (You can add them back once those projects exist.)
COPY MineralKingdom/MineralKingdom.Domain/MineralKingdom.Domain.csproj MineralKingdom/MineralKingdom.Domain/
COPY MineralKingdom/MineralKingdom.Infrastructure/MineralKingdom.Infrastructure.csproj MineralKingdom/MineralKingdom.Infrastructure/
COPY MineralKingdom/MineralKingdom.Contracts/MineralKingdom.Contracts.csproj MineralKingdom/MineralKingdom.Contracts/

RUN dotnet restore MineralKingdom/MineralKingdom.sln

# Copy everything and publish
COPY . ./

RUN dotnet publish MineralKingdom/MineralKingdom.Api/MineralKingdom.Api.csproj -c Release -o /out/api
RUN dotnet publish MineralKingdom/MineralKingdom.Worker/MineralKingdom.Worker.csproj -c Release -o /out/worker

# ---------- runtime: api ----------
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS api
WORKDIR /app
COPY --from=build /out/api ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MineralKingdom.Api.dll"]

# ---------- runtime: worker ----------
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS worker
WORKDIR /app
COPY --from=build /out/worker ./
ENTRYPOINT ["dotnet", "MineralKingdom.Worker.dll"]
