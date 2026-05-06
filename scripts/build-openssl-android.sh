#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT_DIR/scripts/android-native-common.sh"

OPENSSL_VERSION="${OPENSSL_VERSION:-3.0.14}"
ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-24}"
OPENSSL_BASE_URL="${OPENSSL_BASE_URL:-https://www.openssl.org/source}"

android_native_select_abi
android_native_find_ndk

BUILD_ROOT="${OPENSSL_BUILD_ROOT:-$ROOT_DIR/artifacts/android-native/openssl-$OPENSSL_VERSION-$ANDROID_RID}"
DOWNLOAD_DIR="$BUILD_ROOT/downloads"
SOURCE_PARENT_DIR="$BUILD_ROOT/src"
SOURCE_DIR="$SOURCE_PARENT_DIR/openssl-$OPENSSL_VERSION"
INSTALL_DIR="${OPENSSL_OUTPUT_DIR:-$BUILD_ROOT/prefix}"
ARCHIVE_PATH="$DOWNLOAD_DIR/openssl-$OPENSSL_VERSION.tar.gz"
OPENSSL_MAKE_JOBS="${OPENSSL_MAKE_JOBS:-$(android_native_cpu_count)}"

ensure_perl() {
  if perl -MLocale::Maketext::Simple -MExtUtils::MakeMaker -MPod::Usage -e1 >/dev/null 2>&1; then
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

TOOLCHAIN_DIR="$(android_native_toolchain_dir)"

if [ -f "$INSTALL_DIR/lib/libssl.so.3" ] && [ -f "$INSTALL_DIR/lib/libcrypto.so.3" ] && [ "${FORCE_REBUILD:-0}" != "1" ]; then
  echo "Reusing existing Android OpenSSL $ANDROID_ABI build in $INSTALL_DIR"
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
export CC="$TOOLCHAIN_DIR/bin/${ANDROID_CLANG_TARGET}${ANDROID_API_LEVEL}-clang"
export AR="$TOOLCHAIN_DIR/bin/llvm-ar"
export RANLIB="$TOOLCHAIN_DIR/bin/llvm-ranlib"
export STRIP="$TOOLCHAIN_DIR/bin/llvm-strip"

if [[ "$(uname -s)" =~ ^(MINGW|MSYS|CYGWIN) ]]; then
  export SHELL="${SHELL:-$(command -v sh)}"
  export MAKESHELL="$SHELL"
fi

ensure_perl

pushd "$SOURCE_DIR" >/dev/null
perl ./Configure \
  "$ANDROID_OPENSSL_TARGET" \
  "-D__ANDROID_API__=$ANDROID_API_LEVEL" \
  shared \
  no-tests \
  no-unit-test \
  --prefix="$INSTALL_DIR" \
  --openssldir="$INSTALL_DIR/ssl"

if [ ! -f Makefile ]; then
  echo "OpenSSL Configure did not produce $SOURCE_DIR/Makefile"
  exit 1
fi

if [[ "$(uname -s)" =~ ^(MINGW|MSYS|CYGWIN) ]]; then
  make -j"$OPENSSL_MAKE_JOBS" \
    build_generated \
    libcrypto.a \
    libssl.a \
    libcrypto.ld \
    libssl.ld \
    crypto/libssl-shlib-packet.o \
    ssl/libdefault-lib-s3_cbc.o \
    ssl/record/libcommon-lib-tls_pad.o

  for object_file in \
    crypto/libssl-shlib-packet.o \
    ssl/libdefault-lib-s3_cbc.o \
    ssl/record/libcommon-lib-tls_pad.o; do
    if [ ! -f "$object_file" ]; then
      echo "Expected OpenSSL object was not generated: $SOURCE_DIR/$object_file"
      exit 1
    fi
  done

  for generated_file in libcrypto.ld libssl.ld; do
    if [ ! -f "$generated_file" ]; then
      echo "Expected OpenSSL version script was not generated: $SOURCE_DIR/$generated_file"
      exit 1
    fi
  done

  "$TOOLCHAIN_DIR/bin/${ANDROID_CLANG_TARGET}${ANDROID_API_LEVEL}-clang" \
    -fPIC \
    -pthread \
    -Wa,--noexecstack \
    -Qunused-arguments \
    -Wall \
    -O3 \
    -Wl,-znodelete \
    -shared \
    -Wl,-Bsymbolic \
    -Wl,--no-undefined \
    -Wl,-soname=libcrypto.so.3 \
    -o libcrypto.so.3 \
    -Wl,--version-script=libcrypto.ld \
    -Wl,--whole-archive libcrypto.a \
    -Wl,--no-whole-archive \
    -ldl \
    -pthread

  "$TOOLCHAIN_DIR/bin/${ANDROID_CLANG_TARGET}${ANDROID_API_LEVEL}-clang" \
    -fPIC \
    -pthread \
    -Wa,--noexecstack \
    -Qunused-arguments \
    -Wall \
    -O3 \
    -Wl,-znodelete \
    -shared \
    -Wl,-Bsymbolic \
    -Wl,--no-undefined \
    -Wl,-soname=libssl.so.3 \
    -o libssl.so.3 \
    -Wl,--version-script=libssl.ld \
    crypto/libssl-shlib-packet.o \
    ssl/libdefault-lib-s3_cbc.o \
    ssl/record/libcommon-lib-tls_pad.o \
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
  make -j"$OPENSSL_MAKE_JOBS" build_generated libcrypto.so libssl.so

  mkdir -p "$INSTALL_DIR/lib" "$INSTALL_DIR/include"
  cp -R include/openssl "$INSTALL_DIR/include/"
  install -m 0644 libcrypto.so "$INSTALL_DIR/lib/libcrypto.so.3"
  install -m 0644 libssl.so "$INSTALL_DIR/lib/libssl.so.3"
fi
popd >/dev/null

READELF="$(android_native_readelf "$TOOLCHAIN_DIR")"
if [ -n "$READELF" ] &&
  "$READELF" --dyn-syms "$INSTALL_DIR/lib/libssl.so.3" |
    grep -E 'UND .* (ssl3_cbc_remove_padding_and_mac|tls1_cbc_remove_padding_and_mac|ssl3_cbc_digest_record)$' >/dev/null; then
  echo "Built libssl.so.3 still contains unresolved OpenSSL CBC symbols."
  exit 1
fi

"$STRIP" --strip-unneeded "$INSTALL_DIR/lib/libssl.so.3" "$INSTALL_DIR/lib/libcrypto.so.3"
echo "Wrote $INSTALL_DIR/lib/libssl.so.3"
echo "Wrote $INSTALL_DIR/lib/libcrypto.so.3"
