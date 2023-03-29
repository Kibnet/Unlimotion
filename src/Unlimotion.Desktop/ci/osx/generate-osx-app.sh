#!/usr/bin/env bash

APP_NAME="./Unlimotion.app"
PUBLISH_OUTPUT_DIRECTORY="./src/Unlimotion.Desktop/bin/Release/net6.0/osx-x64/publish/."

# PUBLISH_OUTPUT_DIRECTORY should point to the output directory of your dotnet publish command.
# One example is /bin/Release/net6.0/osx-x64/publish/.
# If you want to change output directories, add `--output /my/directory/path` to your `dotnet publish` command.

INFO_PLIST="./src/Unlimotion.Desktop/ci/osx/Info.plist"
ICON_FILE="./src/Unlimotion.Desktop/Assets/Unlimotion.icns"
VERSION=$1

sed -i '' "s/CFBundleVersionExample/$VERSION/g" $INFO_PLIST
sed -i '' "s/CFBundleShortVersionStringExample/$VERSION/g" $INFO_PLIST

if [ -d "$APP_NAME" ]
then
    rm -rf "$APP_NAME"
fi

mkdir "$APP_NAME"

mkdir "$APP_NAME/Contents"
mkdir "$APP_NAME/Contents/MacOS"
mkdir "$APP_NAME/Contents/Resources"

cp "$INFO_PLIST" "$APP_NAME/Contents/Info.plist"
cp "$ICON_FILE" "$APP_NAME/Contents/Resources/Unlimotion.icns"
cp -a "$PUBLISH_OUTPUT_DIRECTORY" "$APP_NAME/Contents/MacOS"