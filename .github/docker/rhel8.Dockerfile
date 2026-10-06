# Build image for the glibc Linux binaries (linux-x64, linux-arm64).
#
# AlmaLinux 8 is binary-compatible with RHEL 8 and has its glibc, 2.28. Testing and smoke testing
# here proves the binaries run on RHEL 8, which is in .NET 10's supported list
# (https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).
#
# The SDK is the latest .NET 10 SDK when the image is built. CI caches the image, so it moves to a
# newer AlmaLinux 8 and 10.0 SDK when the Dockerfile changes or the cache entry expires.
FROM almalinux:8

# The libraries .NET needs on RHEL (https://learn.microsoft.com/dotnet/core/install/linux-rhel#dependencies)
# and the tools that download and unpack the SDK.
RUN dnf install -y glibc libgcc ca-certificates openssl-libs libstdc++ libicu tzdata krb5-libs \
      curl tar gzip findutils \
 && dnf clean all

# BuildKit sets TARGETARCH to the architecture the image is built for (amd64 or arm64).
ARG TARGETARCH

# aka.ms/dotnet/10.0 redirects to the latest .NET 10 SDK for each platform.
RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "No .NET SDK download for architecture '$TARGETARCH'" >&2; exit 1 ;; \
    esac \
 && mkdir -p /usr/share/dotnet \
 && curl -fsSL "https://aka.ms/dotnet/10.0/dotnet-sdk-$rid.tar.gz" | tar -xz -C /usr/share/dotnet

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH=/usr/share/dotnet:$PATH
