# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY GitHubAutomationBot.sln ./
COPY src/GitHubBot.Domain/*.csproj ./src/GitHubBot.Domain/
COPY src/GitHubBot.Application/*.csproj ./src/GitHubBot.Application/
COPY src/GitHubBot.Infrastructure/*.csproj ./src/GitHubBot.Infrastructure/
COPY src/GitHubBot.Api/*.csproj ./src/GitHubBot.Api/
COPY tests/GitHubBot.UnitTests/*.csproj ./tests/GitHubBot.UnitTests/
COPY tests/GitHubBot.IntegrationTests/*.csproj ./tests/GitHubBot.IntegrationTests/

RUN dotnet restore

# Copy all source code
COPY src/ ./src/
COPY tests/ ./tests/

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
