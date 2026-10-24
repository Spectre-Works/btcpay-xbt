#!/bin/sh
# Compile on the workstation; build the runtime-only image on an ARM64 host.
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
python3 scripts/prepare.py nbxplorer
mkdir -p .build/nbxplorer-arm64/app
docker run --rm -v "$root/.build/nbxplorer:/source" \
  -v "$root/.build/nbxplorer-arm64/app:/publish" -w /source \
  mcr.microsoft.com/dotnet/sdk:10.0.400-noble \
  dotnet publish NBXplorer/NBXplorer.csproj -c Release -r linux-arm64 \
  --self-contained false -p:UseAppHost=false -o /publish
cp deploy/Dockerfile.nbxplorer-arm64 .build/nbxplorer-arm64/Dockerfile
tar -C .build/nbxplorer-arm64 -czf .build/nbxplorer-arm64.tar.gz Dockerfile app
printf '%s\n' 'ARM64 runtime build context: .build/nbxplorer-arm64.tar.gz'
