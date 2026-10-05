# Build image for the glibc Linux binaries (linux-x64, linux-arm64).
#
# A native binary needs a glibc at least as new as the one it was linked against. Building on
# AlmaLinux 8, which is binary-compatible with RHEL 8 and has its glibc 2.28, makes the binaries
# run on RHEL 8 and every newer distribution. RHEL 8 is in .NET 10's supported list, and .NET 10
# itself needs glibc 2.27 or newer (https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).
FROM almalinux:8

# Native AOT prerequisites on RHEL (https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites),
# plus binutils to strip symbols, glibc-devel to link against glibc, libicu for the SDK, and the
# tools the .NET install script runs.
RUN dnf install -y clang zlib-devel binutils glibc-devel libicu curl tar gzip findutils \
 && dnf clean all

# The latest .NET 10 SDK, matching what setup-dotnet installs from global.json on the other runners.
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH=/usr/share/dotnet:$PATH
