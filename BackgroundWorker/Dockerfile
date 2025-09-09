FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy solution and project files for restore (requires build context at repo root)
COPY ["llm-rag.sln", "."]
COPY ["BackgroundWorker/BackgroundWorker.csproj", "BackgroundWorker/"]
COPY ["Contracts/Contracts.csproj", "Contracts/"]
RUN dotnet restore "BackgroundWorker/BackgroundWorker.csproj"
# Copy the rest of the source
COPY . .
RUN dotnet publish "BackgroundWorker/BackgroundWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BackgroundWorker.dll"]
