#!/usr/bin/env bash

CSPROJ_PATH="./Unlimotion.Desktop.ForLinuxBuild.csproj"

dotnet tool install --global dotnet-deb

cd ./src/Unlimotion.Desktop

export PATH=$HOME/.dotnet/tools:$PATH
dotnet restore $CSPROJ_PATH
dotnet-deb $CSPROJ_PATH install
dotnet publish $CSPROJ_PATH -c Release -f net6.0 -r linux-x64 -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=true
dotnet-deb $CSPROJ_PATH -c Release -f net6.0 -r linux-x64 -o ./ci/deb
