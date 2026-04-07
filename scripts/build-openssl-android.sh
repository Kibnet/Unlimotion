#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-24}"
ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
OPENSSL_BASE_URL="${OPENSSL_BASE_URL:-https://www.openssl.org/source}"
BUILD_ROOT="${OPENSSL_BUILD_ROOT:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-android-arm64}"
DOWNLOAD_DIR="$BUILD_ROOT/downloads"
SOURCE_PARENT_DIR="$BUILD_ROOT/src"
SOURCE_DIR="$SOURCE_PARENT_DIR/openssl-$OPENSSL_VERSION"
INSTALL_DIR="${OPENSSL_OUTPUT_DIR:-$BUILD_ROOT/prefix}"
ARCHIVE_PATH="$DOWNLOAD_DIR/openssl-$OPENSSL_VERSION.tar.gz"

cpu_count() {
  if command -v nproc >/dev/null 2>&1; then
    nproc
    return
  fi

  sysctl -n hw.ncpu
}

host_tag() {
  case "$(uname -s)" in
    Linux)
      echo "linux-x86_64"
      ;;
    Darwin)
      if [ "$(uname -m)" = "arm64" ]; then
        echo "darwin-arm64"
      else
        echo "darwin-x86_64"
      fi
      ;;
    MINGW*|MSYS*|CYGWIN*)
      echo "windows-x86_64"
      ;;
    *)
      echo "Unsupported host OS: $(uname -s)" >&2
      exit 1
      ;;
  esac
}

ensure_perl() {
  if perl -MLocale::Maketext::Simple -e1 >/dev/null 2>&1; then
    return
  fi

  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
      local portable_perl_root="$ROOT_DIR/artifacts/tools/strawberry-perl/perl"
      local portable_perl_lib="$ROOT_DIR/artifacts/tools/perl-lib"
      if [ -f "$portable_perl_root/lib/Locale/Maketext/Simple.pm" ]; then
        if [ ! -f "$portable_perl_lib/Locale/Maketext/Simple.pm" ] || [ ! -f "$portable_perl_lib/ExtUtils/MakeMaker.pm" ] || [ ! -f "$portable_perl_lib/Pod/Usage.pm" ]; then
          mkdir -p "$portable_perl_lib"
          cp -R "$portable_perl_root/lib/Locale" "$portable_perl_lib/"
          cp -R "$portable_perl_root/lib/ExtUtils" "$portable_perl_lib/"
          cp -R "$portable_perl_root/lib/Pod" "$portable_perl_lib/"
        fi

        export PERL5LIB="$portable_perl_lib${PERL5LIB:+:$PERL5LIB}"
        if perl -MLocale::Maketext::Simple -MExtUtils::MakeMaker -MPod::Usage -e1 >/dev/null 2>&1; then
          return
        fi
      fi

      echo "A Perl distribution with Locale::Maketext::Simple is required to build Android OpenSSL on Windows."
      echo "Place a portable Strawberry Perl under artifacts/tools/strawberry-perl so Git Bash perl can reuse Locale and ExtUtils modules."
      exit 1
      ;;
    *)
      echo "Perl is missing the Locale::Maketext::Simple module required by OpenSSL Configure."
      exit 1
      ;;
  esac
}

if [ -z "$ANDROID_SDK_ROOT" ]; then
  echo "ANDROID_SDK_ROOT or ANDROID_HOME must be set"
  exit 1
fi

if [ -z "${ANDROID_NDK_ROOT:-}" ]; then
  if [ -d "$ANDROID_SDK_ROOT/ndk" ]; then
    ANDROID_NDK_ROOT="$(ls -d "$ANDROID_SDK_ROOT/ndk/"* 2>/dev/null | sort -V | tail -1)"
  elif [ -d "$ANDROID_SDK_ROOT/ndk-bundle" ]; then
    ANDROID_NDK_ROOT="$ANDROID_SDK_ROOT/ndk-bundle"
  else
    echo "ANDROID_NDK_ROOT not set and no NDK found under $ANDROID_SDK_ROOT"
    exit 1
  fi
fi

TOOLCHAIN_DIR="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/$(host_tag)"
if [ ! -d "$TOOLCHAIN_DIR" ]; then
  echo "Android LLVM toolchain not found: $TOOLCHAIN_DIR"
  exit 1
fi

if [ -f "$INSTALL_DIR/lib/libssl.so.3" ] && [ -f "$INSTALL_DIR/lib/libcrypto.so.3" ] && [ "${FORCE_REBUILD:-0}" != "1" ]; then
  echo "Reusing existing Android OpenSSL build in $INSTALL_DIR"
  exit 0
fi

mkdir -p "$DOWNLOAD_DIR" "$SOURCE_PARENT_DIR"

if [ ! -f "$ARCHIVE_PATH" ]; then
  curl -fL "$OPENSSL_BASE_URL/openssl-$OPENSSL_VERSION.tar.gz" -o "$ARCHIVE_PATH"
fi

rm -rf "$SOURCE_DIR" "$INSTALL_DIR"
tar -xzf "$ARCHIVE_PATH" -C "$SOURCE_PARENT_DIR"

if [[ "$(uname -s)" =~ ^(MINGW|MSYS|CYGWIN) ]] && [ -f "$SOURCE_DIR/Configure" ]; then
  perl -0pi -e 's/\$target\{exe_extension\}="\.pm"  if \(\$config\{target\} =~ \/vos\/\);/\$target{exe_extension}=".pm"  if (\$config{target} =~ \/vos\/);\n\$target{exe_extension}=".exe" if (\$^O eq "MSWin32" \&\& !\$target{exe_extension});/g' "$SOURCE_DIR/Configure"
  perl -0pi -e 's/if \(eval \{ require IPC::Cmd; 1; \}\) \{/if (0 \&\& eval { require IPC::Cmd; 1; }) {/g; s/foreach \(File::Spec->path\(\)\) \{\n            my \$fullpath = catfile\(\$_, "\$name\$target\{exe_extension\}"\);\n            if \(-f \$fullpath and -x \$fullpath\) \{\n                return \$fullpath;\n            \}\n        \}/foreach (File::Spec->path()) {\n            foreach my \$fullpath (catfile(\$_, "\$name\$target{exe_extension}"), catfile(\$_, "\$name.exe"), catfile(\$_, \$name)) {\n                next unless -f \$fullpath and -x \$fullpath;\n                \$fullpath =~ s{\\\\}{\/}g;\n                return \$fullpath;\n            }\n        }/g; s/return \$fullpath;/\$fullpath =~ s{\\\\}{\/}g;\n                return \$fullpath;/g' "$SOURCE_DIR/Configure"
  perl -0pi -e 's/\$ndk = canonpath\(\$ndk\);/\$ndk = canonpath(\$ndk);\n            \$ndk =~ s{\\\\}{\/}g;/g' "$SOURCE_DIR/Configurations/15-android.conf"
fi

export PATH="$TOOLCHAIN_DIR/bin:$PATH"
export ANDROID_NDK_ROOT
export MSYS2_ENV_CONV_EXCL="PERL5LIB${MSYS2_ENV_CONV_EXCL:+;$MSYS2_ENV_CONV_EXCL}"
export STRIP="$TOOLCHAIN_DIR/bin/llvm-strip"

if [[ "$(uname -s)" =~ ^(MINGW|MSYS|CYGWIN) ]]; then
  export SHELL="${SHELL:-$(command -v sh)}"
  export MAKESHELL="$SHELL"
fi

ensure_perl

pushd "$SOURCE_DIR" >/dev/null
perl ./Configure \
  android-arm64 \
  "-D__ANDROID_API__=$ANDROID_API_LEVEL" \
  shared \
  no-tests \
  no-unit-test \
  --prefix="$INSTALL_DIR" \
  --openssldir="$INSTALL_DIR/ssl"

if [[ "$(uname -s)" =~ ^(MINGW|MSYS|CYGWIN) ]]; then
  make -j"$(cpu_count)" build_generated libcrypto.a libssl.a libcrypto.ld libssl.ld crypto/libssl-shlib-packet.o

  if [ ! -f "crypto/libssl-shlib-packet.o" ]; then
    echo "Expected OpenSSL shared packet object was not generated: $SOURCE_DIR/crypto/libssl-shlib-packet.o"
    exit 1
  fi

  for generated_file in libcrypto.ld libssl.ld; do
    if [ ! -f "$generated_file" ]; then
      echo "Expected OpenSSL version script was not generated: $SOURCE_DIR/$generated_file"
      exit 1
    fi
  done

  "$TOOLCHAIN_DIR/bin/aarch64-linux-android${ANDROID_API_LEVEL}-clang" \
    -fPIC \
    -pthread \
    -Wa,--noexecstack \
    -Qunused-arguments \
    -Wall \
    -O3 \
    -Wl,-znodelete \
    -shared \
    -Wl,-Bsymbolic \
    -Wl,-soname=libcrypto.so.3 \
    -o libcrypto.so.3 \
    -Wl,--version-script=libcrypto.ld \
    -Wl,--whole-archive libcrypto.a \
    -Wl,--no-whole-archive \
    -ldl \
    -pthread

  "$TOOLCHAIN_DIR/bin/aarch64-linux-android${ANDROID_API_LEVEL}-clang" \
    -fPIC \
    -pthread \
    -Wa,--noexecstack \
    -Qunused-arguments \
    -Wall \
    -O3 \
    -Wl,-znodelete \
    -shared \
    -Wl,-Bsymbolic \
    -Wl,-soname=libssl.so.3 \
    -o libssl.so.3 \
    -Wl,--version-script=libssl.ld \
    crypto/libssl-shlib-packet.o \
    -Wl,--whole-archive libssl.a \
    -Wl,--no-whole-archive \
    ./libcrypto.so.3 \
    -ldl \
    -pthread

  mkdir -p "$INSTALL_DIR/lib" "$INSTALL_DIR/include"
  cp -R include/openssl "$INSTALL_DIR/include/"
  install -m 0644 libcrypto.so.3 "$INSTALL_DIR/lib/libcrypto.so.3"
  install -m 0644 libssl.so.3 "$INSTALL_DIR/lib/libssl.so.3"
else
  # Android packaging only needs the shared libraries and headers; building the
  # full software bundle also pulls in target-side programs we never ship.
  make -j"$(cpu_count)" build_generated libcrypto.so libssl.so

  mkdir -p "$INSTALL_DIR/lib" "$INSTALL_DIR/include"
  cp -R include/openssl "$INSTALL_DIR/include/"
  install -m 0644 libcrypto.so "$INSTALL_DIR/lib/libcrypto.so.3"
  install -m 0644 libssl.so "$INSTALL_DIR/lib/libssl.so.3"
fi
popd >/dev/null

"$STRIP" --strip-unneeded "$INSTALL_DIR/lib/libssl.so.3" "$INSTALL_DIR/lib/libcrypto.so.3"
echo "Wrote $INSTALL_DIR/lib/libssl.so.3"
echo "Wrote $INSTALL_DIR/lib/libcrypto.so.3"
