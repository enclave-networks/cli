# enclave-cli

**Fully manage and automate the Enclave platform from your terminal.**

`enclave-cli` is a command-line interface for the [Enclave Platform API](https://api.enclave.io), built for agents and  MSP operators who want to manage resources, automate workflows, and integrate Enclave into their existing tooling — without writing API integrations from scratch.

## Get started

1. Download `enclave-cli` for your platform from the [latest release](https://github.com/enclave-networks/cli/releases/latest).
2. Create a personal access token in the [Enclave portal](https://portal.enclave.io/).
3. Use `enclave-cli login` to log in and save the Personal access token to your user directory (this only needs to be done once).

   **On Windows:**

   ```cmd
   echo <your token> | .\enclave-cli login --token-stdin
   ```

   Your personal access token will be saved to `%USERPROFILE%\.enclave\credentials.json`

   **On Linux and macOS:**

   ```bash
   echo '<your token>' | ./enclave-cli login --token-stdin
   ```

   Your personal access token will be saved to `~/.enclave/credentials.json`

## Example usage

1. List the organisations you have access to:
   ```
   enclave-cli org list
   ```

For the full CLI reference, see [docs.enclave.io](https://docs.enclave.io).
