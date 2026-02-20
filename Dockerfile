# 1. Build Phase
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for restoring dependencies
COPY ["src/Draco.Domain/Draco.Domain.csproj", "src/Draco.Domain/"]
COPY ["src/Draco.Application/Draco.Application.csproj", "src/Draco.Application/"]
COPY ["src/Draco.Infrastructure/Draco.Infrastructure.csproj", "src/Draco.Infrastructure/"]
COPY ["src/Draco.Api/Draco.Api.csproj", "src/Draco.Api/"]

# Restore
RUN dotnet restore "src/Draco.Api/Draco.Api.csproj"

# Copy the rest of the source code
COPY src/ src/

# Publish
WORKDIR "/src/src/Draco.Api"
RUN dotnet publish "Draco.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Runtime Phase
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Railway provides the PORT environment variable dynamically at runtime.
# We map ASP.NET Core's URL configuration to whatever port Railway decides to assign us.
# (If PORT isn't set, default to 8080)
ENTRYPOINT ["sh", "-c", "dotnet Draco.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
