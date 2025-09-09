FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy solution and project files for restore (requires build context at repo root)
COPY ["llm-rag.sln", "."]
COPY ["WebService/WebService.csproj", "WebService/"]
COPY ["Contracts/Contracts.csproj", "Contracts/"]
RUN dotnet restore "WebService/WebService.csproj"
# Copy the rest of the source
COPY . .
RUN dotnet publish "WebService/WebService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WebService.dll"]
