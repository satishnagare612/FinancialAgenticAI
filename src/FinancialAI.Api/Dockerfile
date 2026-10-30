# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the csproj files first so restore layer caches across source edits.
# ProjectReference paths resolve from repo root, so preserve the src/ layout.
COPY src/FinancialAI.Domain/FinancialAI.Domain.csproj src/FinancialAI.Domain/
COPY src/FinancialAI.Application/FinancialAI.Application.csproj src/FinancialAI.Application/
COPY src/FinancialAI.Infrastructure/FinancialAI.Infrastructure.csproj src/FinancialAI.Infrastructure/
COPY src/FinancialAI.Api/FinancialAI.Api.csproj src/FinancialAI.Api/

RUN dotnet restore src/FinancialAI.Api/FinancialAI.Api.csproj

COPY src/ src/

WORKDIR /src/src/FinancialAI.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# QuestPDF native dependencies (see Issue 7). Harmless to include now;
# without them, PersistAndRenderExecutor throws at Render() call time.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        libfontconfig1 libharfbuzz0b libfreetype6 \
 && rm -rf /var/lib/apt/lists/*

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FinancialAI.Api.dll"]