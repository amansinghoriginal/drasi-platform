# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files
COPY mcp-reaction.sln ./
COPY Drasi.Reactions.Mcp/Drasi.Reactions.Mcp.csproj ./Drasi.Reactions.Mcp/

# Restore dependencies
RUN dotnet restore "./Drasi.Reactions.Mcp/Drasi.Reactions.Mcp.csproj"

# Copy only the source code
COPY Drasi.Reactions.Mcp/ ./Drasi.Reactions.Mcp/

# Build the reaction project
WORKDIR /src/Drasi.Reactions.Mcp
RUN dotnet publish "./Drasi.Reactions.Mcp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set log levels for reaction in production
ENV Logging__LogLevel__Default="Information"
ENV Logging__LogLevel__Microsoft="Warning"
ENV Logging__LogLevel__Microsoft_Hosting_Lifetime="Information"
ENV Logging__LogLevel__Drasi_Reactions_Mcp="Information"

# MCP server configuration
ENV MCP_PORT=8080

# Expose MCP HTTP port (Drasi handles APP_PORT internally)
EXPOSE 8080

ENTRYPOINT ["dotnet", "Drasi.Reactions.Mcp.dll"]