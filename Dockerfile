# Multi-stage Dockerfile for FlowSharp (Web and Worker)

# Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# SDK image for restoring and building
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files for restoring
COPY ["src/FlowSharp.Web/FlowSharp.Web.csproj", "src/FlowSharp.Web/"]
COPY ["src/FlowSharp.Worker/FlowSharp.Worker.csproj", "src/FlowSharp.Worker/"]
COPY ["src/FlowSharp.Domain/FlowSharp.Domain.csproj", "src/FlowSharp.Domain/"]
COPY ["src/FlowSharp.Application/FlowSharp.Application.csproj", "src/FlowSharp.Application/"]
COPY ["src/FlowSharp.Infrastructure/FlowSharp.Infrastructure.csproj", "src/FlowSharp.Infrastructure/"]
COPY ["src/FlowSharp.Nodes/FlowSharp.Nodes.csproj", "src/FlowSharp.Nodes/"]

# Restore dependencies
RUN dotnet restore "src/FlowSharp.Web/FlowSharp.Web.csproj"
RUN dotnet restore "src/FlowSharp.Worker/FlowSharp.Worker.csproj"

# Copy all source files
COPY . .

# Build Web
WORKDIR "/src/src/FlowSharp.Web"
RUN dotnet build "FlowSharp.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Build Worker
WORKDIR "/src/src/FlowSharp.Worker"
RUN dotnet build "FlowSharp.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish Web stage
FROM build AS publish-web
WORKDIR "/src/src/FlowSharp.Web"
RUN dotnet publish "FlowSharp.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish/web /p:UseAppHost=false

# Publish Worker stage
FROM build AS publish-worker
WORKDIR "/src/src/FlowSharp.Worker"
RUN dotnet publish "FlowSharp.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish/worker /p:UseAppHost=false

# Final Web target image
FROM base AS web
WORKDIR /app
COPY --from=publish-web /app/publish/web .
# Create folders for plugins and RAG DBs
RUN mkdir -p plugins logs rag_db
ENTRYPOINT ["dotnet", "FlowSharp.Web.dll"]

# Final Worker target image
FROM base AS worker
WORKDIR /app
COPY --from=publish-worker /app/publish/worker .
RUN mkdir -p logs rag_db
ENTRYPOINT ["dotnet", "FlowSharp.Worker.dll"]
