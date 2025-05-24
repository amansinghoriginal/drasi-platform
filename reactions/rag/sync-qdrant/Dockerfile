# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files
COPY sync-qdrant.sln ./
COPY Drasi.Reactions.SyncQdrant/Drasi.Reactions.SyncQdrant.csproj ./Drasi.Reactions.SyncQdrant/

# Restore dependencies
RUN dotnet restore "./Drasi.Reactions.SyncQdrant/Drasi.Reactions.SyncQdrant.csproj"

# Copy only the source code
COPY Drasi.Reactions.SyncQdrant/ ./Drasi.Reactions.SyncQdrant/

# Build the reaction project
WORKDIR /src/Drasi.Reactions.SyncQdrant
RUN dotnet publish "./Drasi.Reactions.SyncQdrant.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set log levels for reaction in production
ENV Logging__LogLevel__Default="Debug"
ENV Logging__LogLevel__Microsoft="Warning"
ENV Logging__LogLevel__Microsoft_Hosting_Lifetime="Information"
ENV Logging__LogLevel__Drasi_Reactions_SyncQdrant="Debug"

USER app
ENTRYPOINT ["dotnet", "Drasi.Reactions.SyncQdrant.dll"]