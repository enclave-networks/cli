# cli

Manage Enclave from the command line.

1. Download the archive for your platform from the [latest release](https://github.com/enclave-networks/cli/releases/latest), and extract it.
2. Create a personal access token in the Enclave portal.
3. Use `enclave-cli login` to save the token to your user directory, this only needs to be done once.

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

4. List the organisations you have access to:
   ```
   enclave-cli org list
   ```

For the full CLI reference, see [docs.enclave.io](https://docs.enclave.io).
