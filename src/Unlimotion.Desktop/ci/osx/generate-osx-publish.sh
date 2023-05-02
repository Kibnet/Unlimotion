#!/usr/bin/env bash

CSPROJ_PATH="./src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacBuild.csproj"

dotnet restore $CSPROJ_PATH
dotnet publish $CSPROJ_PATH -c Release -f net7.0 -r osx-x64 -p:Version=$1 -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=true

