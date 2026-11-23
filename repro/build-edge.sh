#!/usr/bin/env bash
# Build two linux-x64 shared-framework directories for run.sh. Use a fresh output directory.
set -euo pipefail
edge="${1:?use december, january, january-wait, or release-fix}"
out="${2:?absolute output directory}"
scriptdir=$(cd "$(dirname "$0")" && pwd)
case "$edge" in
    december) version=11.0.0-alpha.1.26059.114; commit=421445ed64461414dc1e99c6716e81e0b1c51b44; subset=clr.runtime+clr.corelib+libs.native ;;
    january|january-wait) version=11.0.0-alpha.1.26065.110; commit=c14473701d9106c7be977b25b90211b99231b54b; subset=clr.corelib ;;
    release-fix) version=11.0.0; commit=deae2e32bf0e018e09d64597b7c59b62b73bb3ce; subset=clr.corelib ;;
    *) exit 2 ;;
esac
[[ "$out" = /* && ! -e "$out" ]] || { echo 'Choose a new absolute output directory' >&2; exit 2; }
mkdir -p "$out/cache" "$out/packages"
cache="${DOTNET_REPRO_BUILD_CACHE:-$out/cache}"
mkdir -p "$cache"
git init -q "$out/runtime"
git -C "$out/runtime" remote add origin https://github.com/dotnet/runtime.git
git -C "$out/runtime" fetch --depth=2 --filter=blob:none origin "$commit"
feed=https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/flat2
if [[ "$edge" == release-fix ]]; then feed=https://api.nuget.org/v3-flatcontainer; fi
for package in microsoft.netcore.app.runtime.linux-x64 microsoft.netcore.app.crossgen2.linux-x64; do
    archive="$out/packages/$package.zip"
    curl --fail --location --retry 3 "$feed/$package/$version/$package.$version.nupkg" -o "$archive"
    unzip -qo "$archive" -d "$out/packages/$package"
done
runtime="$out/packages/microsoft.netcore.app.runtime.linux-x64/runtimes/linux-x64"
cg="$out/packages/microsoft.netcore.app.crossgen2.linux-x64/tools"
chmod +x "$cg/crossgen2"
builder=mcr.microsoft.com/dotnet-buildtools/prereqs:ubuntu-22.04-amd64
for arm in A B; do
    rev="$commit"
    if [[ "$arm" == A && ( "$edge" == january || "$edge" == december ) ]]; then rev="$commit^"; fi
    git -C "$out/runtime" worktree add --quiet --detach "$out/source-$arm" "$rev"
    if [[ "$arm" == B && "$edge" == release-fix ]]; then
        git -C "$out/source-$arm" apply "$scriptdir/v11-comparison.patch"
    elif [[ "$arm" == B && "$edge" == january-wait ]]; then
        git -C "$out/source-$arm" apply "$scriptdir/january-wait-only.patch"
    fi
    dst="$out/$arm"
    mkdir "$dst"
    # Both arms use the same daily runtime and compiler; replace only components affected by the commit.
    cp -a "$runtime/native/." "$dst/"
    lib=("$runtime"/lib/net*)
    [[ "${#lib[@]}" == 1 ]]
    cp -a "${lib[0]}/." "$dst/"
    # Worktrees point to the common Git directory by absolute path. Keep that path valid in the builder.
    docker run --rm --user "$(id -u):$(id -g)" --volume "$cache:/cache" --volume "$out:$out" \
        -e HOME=/cache -e NUGET_PACKAGES=/cache/packages -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -w "$out/source-$arm" \
        "$builder" bash ./build.sh "$subset" -c Release
    core="$out/source-$arm/artifacts/bin/coreclr/linux.x64.Release"
    if [[ "$edge" == december ]]; then
        cp "$core/libcoreclr.so" "$core/libclrjit.so" "$dst/"
        native=("$out/source-$arm"/artifacts/bin/native/net11.0-linux-Release-x64/libSystem.Native.so)
        [[ -f "${native[0]}" ]] || { mapfile -t native < <(find "$out/source-$arm/artifacts/bin/native/net11.0-linux-Release-x64" -name libSystem.Native.so); }
        [[ "${#native[@]}" == 1 && -f "${native[0]}" ]] && cp "${native[0]}" "$dst/"
        cmp "${native[0]}" "$dst/libSystem.Native.so"
    fi
    docker run --rm --network none --user "$(id -u):$(id -g)" -v "$core/IL:/il:ro" -v "$cg:/cg:ro" -v "$dst:/out" \
        --entrypoint /cg/crossgen2 mcr.microsoft.com/dotnet/aspnet:11.0.0 \
        /il/System.Private.CoreLib.dll --targetarch:x64 --targetos:linux -O --inputbubble --parallelism 2 -o:/out/System.Private.CoreLib.dll
    git -C "$out/source-$arm" rev-parse HEAD
    (cd "$dst" && sha256sum libcoreclr.so libclrjit.so libSystem.Native.so System.Private.CoreLib.dll)
done
printf 'Use FX_A=%q FX_B=%q ORDER="A B B A" bash run.sh\n' "$out/A" "$out/B"
