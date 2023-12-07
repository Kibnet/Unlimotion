#!/usr/bin/env bash

CSPROJ_PATH="./Unlimotion.Desktop.ForDebianBuild.csproj"

dotnet tool install --global dotnet-deb

cd ./src/Unlimotion.Desktop

export PATH=$HOME/.dotnet/tools:$PATH
dotnet restore $CSPROJ_PATH
dotnet-deb $CSPROJ_PATH install
dotnet msbuild $CSPROJ_PATH -t:CreateDeb -p:Version=$1 -p:Configuration=Release -p:TargetFramework=net8.0 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false