#!/usr/bin/env bash
# Prepares a Linux host (or CI runner) to run the Hardened sandbox lifecycle lane:
# DockerHardenedSandboxLifecycleTests. Without this, that lane skips with a precise diagnostic
# rather than silently degrading to an ordinary runtime.
#
# It installs gVisor, registers it with the Docker daemon, and gives the locally built worker image
# a real registry digest through a loopback registry, because Hardened mode refuses a mutable tag or
# a local image ID by design.
#
# Usage:  sudo bash scripts/enable-hardened-sandbox-lane.sh [worker-image-tag]
# Then:   export ETLSQL_SANDBOX_WORKER_DIGEST_IMAGE=$(cat "$PINNED_FILE")
set -euo pipefail

WORKER_TAG="${1:-etlsql-sandbox-worker-test:local}"
REGISTRY_PORT="${ETLSQL_SANDBOX_REGISTRY_PORT:-5000}"
REGISTRY_HOST="127.0.0.1:${REGISTRY_PORT}"
REPO="localhost:${REGISTRY_PORT}/etlsql-sandbox-worker"
PINNED_FILE="${ETLSQL_SANDBOX_PINNED_FILE:-/tmp/etlsql-pinned-worker-image}"

if [ "$(id -u)" -ne 0 ]; then
    echo "This script installs packages and restarts Docker; run it with sudo." >&2
    exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
    echo "Docker is required but not installed." >&2
    exit 1
fi

if ! command -v runsc >/dev/null 2>&1; then
    echo "==> installing gVisor"
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg >/dev/null
    curl -fsSL https://gvisor.dev/archive.key | gpg --dearmor -o /usr/share/keyrings/gvisor-archive-keyring.gpg
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/gvisor-archive-keyring.gpg] https://storage.googleapis.com/gvisor/releases release main" \
        > /etc/apt/sources.list.d/gvisor.list
    apt-get update -qq
    apt-get install -y -qq runsc >/dev/null
fi

echo "==> registering runsc with the Docker daemon"
runsc install
systemctl restart docker
sleep 5
docker info --format '{{json .Runtimes}}' | grep -q '"runsc"' \
    || { echo "runsc did not register with the daemon." >&2; exit 1; }

echo "==> verifying the hardened runtime actually runs a container"
docker run --rm --runtime runsc alpine:latest true

echo "==> pinning ${WORKER_TAG} to a registry digest"
docker image inspect "$WORKER_TAG" >/dev/null 2>&1 \
    || { echo "Build the worker image first (scripts/Test-SandboxWorkerImage.ps1)." >&2; exit 1; }
docker rm -f etlsql-sandbox-registry >/dev/null 2>&1 || true
docker run -d --name etlsql-sandbox-registry -p "${REGISTRY_HOST}:5000" registry:2 >/dev/null
for _ in $(seq 1 30); do
    curl -fsS "http://${REGISTRY_HOST}/v2/" >/dev/null 2>&1 && break
    sleep 1
done

docker tag "$WORKER_TAG" "${REPO}:v1"
docker push "${REPO}:v1" >/dev/null
PINNED=$(docker image inspect --format '{{range .RepoDigests}}{{println .}}{{end}}' "${REPO}:v1" \
    | grep "^localhost:${REGISTRY_PORT}/" | head -1)
[ -n "$PINNED" ] || { echo "The image did not receive a registry digest." >&2; exit 1; }
printf '%s' "$PINNED" > "$PINNED_FILE"

cat <<EOF

Hardened sandbox lane is ready.

  runsc:        $(runsc --version | head -1)
  pinned image: ${PINNED}

Run the lane with:

  export ETLSQL_SANDBOX_WORKER_DIGEST_IMAGE=\$(cat ${PINNED_FILE})
  dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj \\
      --filter "FullyQualifiedName~DockerHardenedSandboxLifecycleTests" -m:1

Evidence from this lane is Hardened. Evidence from DockerStandardSandboxLifecycleTests is not.
EOF
