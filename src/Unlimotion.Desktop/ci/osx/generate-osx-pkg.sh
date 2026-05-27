#!/usr/bin/env bash

APP_PATH="./Unlimotion.app"
VERSION=$1
RUNTIME=${2:-osx-x64}

productbuild --component $APP_PATH /Applications ./Unlimotion-$VERSION-$RUNTIME.pkg
