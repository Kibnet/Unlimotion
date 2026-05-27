#!/usr/bin/env bash

CSPROJ_PATH="./src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj"
VERSION=$1
RUNTIME=${2:-osx-x64}

dotnet restore $CSPROJ_PATH --runtime $RUNTIME --ignore-failed-sources
dotnet publish $CSPROJ_PATH -c Release -f net10.0 -r $RUNTIME -p:Version=$VERSION -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --ignore-failed-sources
