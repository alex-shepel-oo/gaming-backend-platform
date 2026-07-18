# Build context is backend/, so Directory.Build.props and
# Directory.Packages.props are reachable for restore. Build with:
#   docker build -f ApiGateway/Dockerfile .
# from backend/, or context: ../backend / dockerfile: ApiGateway/Dockerfile
# in docker-compose.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY ApiGateway/ApiGateway.csproj ApiGateway/
RUN dotnet restore ApiGateway/ApiGateway.csproj

COPY ApiGateway/ ApiGateway/
RUN dotnet publish ApiGateway/ApiGateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
