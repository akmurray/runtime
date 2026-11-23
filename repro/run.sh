#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
tag="socket-readiness-repro:local"
docker build -q -t "$tag" .
work="socket-readiness-$$"
network="$work-net"
volume="$work-counters"
client="$work-client"
server="$work-server"
docker network create --internal "$network" >/dev/null
trap 'docker rm -f "$server" "$client" >/dev/null 2>&1 || true; docker volume rm "$volume" >/dev/null 2>&1 || true; docker network rm "$network" >/dev/null 2>&1 || true' EXIT
docker volume create "$volume" >/dev/null
docker run -d --name "$client" --network "$network" --cpuset-cpus "${DRIVER_CPUS:-6,7}" -v "$volume:/run/bench" -e BENCH_PROCESS_SOCKET=/run/bench/counts --entrypoint sleep "$tag" infinity >/dev/null
for version in ${ORDER:-10 11 11 10}; do
    options=()
    case "$version" in
        10) exe=/opt/net10/dotnet ;;
        11) exe=/usr/share/dotnet/dotnet ;;
        A|B)
            var="FX_$version"
            fx="${!var:?Supply the absolute path to a complete linux-x64 Microsoft.NETCore.App shared framework}"
            for f in libcoreclr.so libclrjit.so System.Private.CoreLib.dll Microsoft.NETCore.App.deps.json; do
                [[ -f "$fx/$f" ]] || { echo "Missing $fx/$f" >&2; exit 2; }
            done
            exe=/usr/share/dotnet/dotnet
            options+=(--mount "type=bind,src=$fx,dst=/usr/share/dotnet/shared/Microsoft.NETCore.App/11.0.0,readonly")
            ;;
        *) echo "Unknown runtime $version" >&2; exit 2 ;;
    esac
    if [[ "${BENCH_PIN_POOL:-0}" == 1 ]]; then
        options+=(-e DOTNET_ThreadPool_ForceMinWorkerThreads=0x100 -e DOTNET_GCDynamicAdaptationMode=0)
    fi
    docker run -d --name "$server" --network "$network" --cpuset-cpus "${SERVER_CPUS:-0-3}" -v "$volume:/run/bench" \
        -e DOTNET_STARTUP_HOOKS=/measure/counter/ProcessProbe.dll -e BENCH_PROCESS_SOCKET=/run/bench/counts \
        "${options[@]}" --entrypoint "$exe" "$tag" /measure/app/SocketRepro.dll server >/dev/null
    ready=false
    for i in {1..60}; do
        if docker exec "$client" bash -c "exec 3<>/dev/tcp/$server/5080 && printf 'GET /json HTTP/1.0\r\nHost: bench\r\n\r\n' >&3 && head -1 <&3" 2>/dev/null | grep -q ' 200 '; then ready=true; break; fi
        sleep 1
    done
    "$ready" || { docker logs "$server"; exit 1; }
    root="${exe%/dotnet}/shared"
    major=11.0.0
    [[ "$version" != 10 ]] || major=10.0.12
    maps=$(docker exec "$server" awk '{print $NF}' /proc/1/maps | sort -u)
    for file in "$root/Microsoft.AspNetCore.App/10.0.12/Microsoft.AspNetCore.Server.Kestrel.Core.dll" \
                "$root/Microsoft.NETCore.App/$major/libcoreclr.so" "$root/Microsoft.NETCore.App/$major/System.Private.CoreLib.dll"; do
        grep -Fxq "$file" <<<"$maps"
        actual=$(docker exec "$server" sha256sum "$file")
        printf '%s\n' "$actual"
        if [[ "$version" == [AB] && "$file" == *Microsoft.NETCore.App* ]]; then
            expected=$(sha256sum "$fx/${file##*/}")
            [[ "${actual%% *}" == "${expected%% *}" ]] || { echo "Runtime identity mismatch: $file" >&2; exit 1; }
        fi
    done
    for path in ${SCENARIOS:-ws dispatch json}; do
        proto=ws
        if [[ "$path" == json ]]; then proto=http; fi
        result=$(docker exec "$client" timeout "$((${WARMUP:-8} + ${SECONDS_PER_RUN:-12} + 60))" /opt/net10/dotnet /measure/app/SocketRepro.dll load "$proto://$server:5080/$path" "${CONNECTIONS:-32}" "${WARMUP:-8}" "${SECONDS_PER_RUN:-12}")
        printf '.NET %s %s %s\n' "$version" "$path" "$result"
    done
    docker rm -f "$server" >/dev/null
done
