#!/usr/bin/env bash

CSPROJ_PATH="./Unlimotion.Desktop.ForDebianBuild.csproj"

cd ./src/Unlimotion.Desktop

dotnet restore $CSPROJ_PATH --runtime linux-x64 --ignore-failed-sources
dotnet msbuild $CSPROJ_PATH -t:CreateDeb -p:Version=$1 -p:Configuration=Release -p:TargetFramework=net10.0 -p:RuntimeIdentifier=linux-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:RestoreIgnoreFailedSources=true
