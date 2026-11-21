FROM clickable/amd64-ut24.04-2.x-arm64:latest
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    qt6-base-dev:arm64 qt6-base-dev-tools \
    qt6-declarative-dev:arm64 qt6-declarative-dev-tools \
    qt6-webengine-dev:arm64 \
    && rm -rf /var/lib/apt/lists/*
