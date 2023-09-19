#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 5004
EXPOSE 5005
EXPOSE 5006

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Unlimotion.Server/Unlimotion.Server.csproj", "Unlimotion.Server/"]
COPY ["Unlimotion.Interface/Unlimotion.Interface.csproj", "Unlimotion.Interface/"]
COPY ["Unlimotion.Server.ServiceInterface/Unlimotion.Server.ServiceInterface.csproj", "Unlimotion.Server.ServiceInterface/"]
COPY ["Unlimotion.Server.ServiceModel/Unlimotion.Server.ServiceModel.csproj", "Unlimotion.Server.ServiceModel/"]
COPY ["Unlimotion.Server.Domain/Unlimotion.Server.Domain.csproj", "Unlimotion.Server.Domain/"]
RUN dotnet restore "Unlimotion.Server/Unlimotion.Server.csproj"
COPY . .
WORKDIR "/src/Unlimotion.Server"
RUN dotnet build "Unlimotion.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Unlimotion.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Unlimotion.Server.dll"]
