#!/bin/bash
# Builds libworldline.so for Android. Requires Bazelisk and an Android NDK
# (ANDROID_NDK_HOME, or the NDK under ANDROID_HOME). Mirrors build_android.bat.

set -e

build()
{
    echo "Building $1"
    mkdir -p "../runtimes/android/$1"
    # 16 KB page alignment is required by Android 15+ devices with 16 KB pages.
    bazel build //worldline:worldline -c opt --platforms=//:$2 --linkopt=-Wl,-z,max-page-size=16384
    chmod +w bazel-bin/worldline/libworldline.so
    cp -f bazel-bin/worldline/libworldline.so "../runtimes/android/$1/"
}

build armeabi-v7a armeabi-v7a
build arm64-v8a arm64-v8a
build x86 android_x86_32
build x86_64 android_x86_64
