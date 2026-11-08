#!/bin/bash
# Strip unwanted ABI native libraries from APK
# Usage: StripAbis.sh <apk-path>

APK_PATH="$1"

if [ -f "$APK_PATH" ]; then
    # Only strip if there are unwanted libs to remove
    if unzip -l "$APK_PATH" | grep -qE "lib/(x86|x86_64|armeabi-v7a)/"; then
        zip -d "$APK_PATH" "lib/x86/*" "lib/x86_64/*" "lib/armeabi-v7a/*"
    fi
fi