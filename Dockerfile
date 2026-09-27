# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY backend/GitHubAutomationBot.sln ./
COPY backend/src/GitHubBot.Domain/*.csproj ./src/GitHubBot.Domain/
COPY backend/src/GitHubBot.Application/*.csproj ./src/GitHubBot.Application/
COPY backend/src/GitHubBot.Infrastructure/*.csproj ./src/GitHubBot.Infrastructure/
COPY backend/src/GitHubBot.Api/*.csproj ./src/GitHubBot.Api/
COPY backend/tests/GitHubBot.UnitTests/*.csproj ./tests/GitHubBot.UnitTests/
COPY backend/tests/GitHubBot.IntegrationTests/*.csproj ./tests/GitHubBot.IntegrationTests/
COPY backend/tests/GitHubBot.ArchitectureTests/*.csproj ./tests/GitHubBot.ArchitectureTests/

RUN dotnet restore

# Copy all source code
COPY backend/src/ ./src/
COPY backend/tests/ ./tests/

# Run unit tests during container build
RUN dotnet test tests/GitHubBot.UnitTests/GitHubBot.UnitTests.csproj -c Release --no-restore

# Publish Api
RUN dotnet publish src/GitHubBot.Api/GitHubBot.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GitHubBot.Api.dll"]
