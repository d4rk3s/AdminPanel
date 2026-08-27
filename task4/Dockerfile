FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["task4.csproj", "./"]
RUN dotnet restore "task4.csproj"
COPY . .
RUN dotnet build "task4.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "task4.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "task4.dll"]