#!/usr/bin/env bash

CSPROJ_PATH="./src/Unlimotion.Desktop/Unlimotion.Desktop.ForMacOSBuild.csproj"

dotnet restore $CSPROJ_PATH
dotnet publish $CSPROJ_PATH -c Release -f net6.0 -r osx-x64 -p:PublishSingleFile=true --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=true
