# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files
COPY query-results-server.sln ./
COPY Drasi.Reactions.McpQueryResultsServer/Drasi.Reactions.McpQueryResultsServer.csproj ./Drasi.Reactions.McpQueryResultsServer/

# Restore dependencies
RUN dotnet restore "./Drasi.Reactions.McpQueryResultsServer/Drasi.Reactions.McpQueryResultsServer.csproj"

# Copy only the source code
COPY Drasi.Reactions.McpQueryResultsServer/ ./Drasi.Reactions.McpQueryResultsServer/

# Build the reaction project
WORKDIR /src/Drasi.Reactions.McpQueryResultsServer
RUN dotnet publish "./Drasi.Reactions.McpQueryResultsServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set log levels for reaction in production
ENV Logging__LogLevel__Default="Information"
ENV Logging__LogLevel__Microsoft="Warning"
ENV Logging__LogLevel__Microsoft_Hosting_Lifetime="Information"
ENV Logging__LogLevel__Drasi_Reactions_McpQueryResultsServer="Information"

# Expose both ports
EXPOSE 80
EXPOSE 8080

ENTRYPOINT ["dotnet", "Drasi.Reactions.McpQueryResultsServer.dll"]