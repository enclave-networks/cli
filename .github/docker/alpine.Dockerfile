# Build image for the musl Linux binaries (linux-musl-x64, linux-musl-arm64).
#
# Alpine Linux uses the musl C library, and a binary for it has to be built with musl. Alpine 3.22
# is the oldest Alpine with a .NET 10 SDK image, so the binaries run on it and on newer releases.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.22

# Native AOT prerequisites on Alpine (https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).
RUN apk add --no-cache clang build-base zlib-dev
