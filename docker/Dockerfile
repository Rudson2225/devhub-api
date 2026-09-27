# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore DevHub.slnx
RUN dotnet build DevHub.slnx -c Release --no-restore

# Publish stage
FROM build AS publish
RUN dotnet publish src/DevHub.Api/DevHub.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
COPY --from=publish /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "DevHub.Api.dll"]