FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Unlimotion.BeforBuild/Unlimotion.BeforBuild.csproj", "Unlimotion.BeforBuild/"]
RUN dotnet restore "Unlimotion.BeforBuild/Unlimotion.BeforBuild.csproj"
COPY . .
WORKDIR "/src/Unlimotion.BeforBuild"
RUN dotnet build "Unlimotion.BeforBuild.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Unlimotion.BeforBuild.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Unlimotion.BeforBuild.dll"]
