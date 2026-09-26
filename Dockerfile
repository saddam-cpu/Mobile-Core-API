# Multi-stage build for ASP.NET Core 8 Web API
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["ScreenSharing.Api.csproj", "./"]
RUN dotnet restore "ScreenSharing.Api.csproj"

# Copy source code and build
COPY . .
RUN dotnet build "ScreenSharing.Api.csproj" -c Release -o /app/build

# Publish release package
FROM build AS publish
RUN dotnet publish "ScreenSharing.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Render dynamically assigns PORT at runtime
ENV PORT=8080
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ScreenSharing.Api.dll"]
