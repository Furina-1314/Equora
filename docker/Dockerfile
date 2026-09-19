# Equora 同步服务器镜像(Linux 多阶段构建)。
# 构建上下文为仓库根目录:docker build -f docker/Dockerfile .
FROM ubuntu:24.04 AS build

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential cmake git curl zip unzip tar pkg-config ca-certificates \
    libssl-dev zlib1g-dev libpq-dev && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .

# vcpkg(锁定同一 baseline,与桌面端一致)。
RUN git clone --depth 1 --filter=blob:none https://github.com/microsoft/vcpkg.git /opt/vcpkg && \
    cd /opt/vcpkg && git fetch --depth 1 origin e6f9e70a29a3e80a1fc510d8503304315447112f && \
    git checkout e6f9e70a29a3e80a1fc510d8503304315447112f && ./bootstrap-vcpkg.sh -disableMetrics

WORKDIR /src/server
ENV VCPKG_ROOT=/opt/vcpkg
RUN cmake -S . -B build-server \
        -DCMAKE_BUILD_TYPE=Release \
        -DEQUORA_BUILD_SERVER=ON \
        -DVCPKG_MANIFEST_DIR=/src/server/Equora.Server \
        -DCMAKE_TOOLCHAIN_FILE=/opt/vcpkg/scripts/buildsystems/vcpkg.cmake && \
    cmake --build build-server --target equora_server -j"$(nproc)"

FROM ubuntu:24.04
RUN apt-get update && apt-get install -y --no-install-recommends \
    libssl3 zlib1g libpq5 ca-certificates && \
    rm -rf /var/lib/apt/lists/* && \
    useradd --system --no-create-home equora

COPY --from=build /src/server/build-server/Equora.Server/equora_server /usr/local/bin/
COPY server/migrations /usr/local/share/equora/migrations

USER equora
EXPOSE 8787
HEALTHCHECK --interval=30s --timeout=3s \
    CMD curl -fs http://localhost:8787/api/v1/health || exit 1
ENTRYPOINT ["equora_server"]
