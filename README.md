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

2. List your customers, after choosing your partner account once:
   ```
   enclave-cli partner use --id <partnerId>
   enclave-cli partner customer list
   ```

3. Find a customer's disconnected systems:
   ```
   enclave-cli system list --org "Acme Ltd" --state disconnected
   ```

4. Approve every system waiting for approval:
   ```
   enclave-cli system list --org "Acme Ltd" --pending | enclave-cli system approve --org "Acme Ltd" -
   ```

5. Create an enrolment key for a new site, so its systems join with the right tags:
   ```
   enclave-cli key create "Leeds office" --org "Acme Ltd" --tags leeds,office
   ```

6. Create a policy that lets laptops reach the file servers over SMB:
   ```
   enclave-cli policy create "laptops to file servers" --org "Acme Ltd" --senders laptops --receivers file-servers --acl tcp:445
   ```

7. Make system `ABCDE` a gateway to the office network, then let laptops reach that network through it:
   ```
   enclave-cli system update ABCDE --org "Acme Ltd" --enable-gateway-for 192.168.1.0/24
   enclave-cli policy create "laptops to office network" --org "Acme Ltd" --senders laptops --gateway ABCDE:192.168.1.0/24 --acl any
   ```

8. Create a disabled policy that gives contractors SSH and RDP access, then turn it on for 8 hours when they need it:
   ```
   enclave-cli policy create contractors --org "Acme Ltd" --senders contractors --receivers servers --acl tcp:22 --acl tcp:3389 --disabled
   enclave-cli policy enable contractors --org "Acme Ltd" --for 8h
   ```

9. Check which systems haven't connected for 90 days would be revoked, then revoke them:
   ```
   enclave-cli system list --org "Acme Ltd" --not-seen-for 90d | enclave-cli system revoke --org "Acme Ltd" - --dry-run
   enclave-cli system list --org "Acme Ltd" --not-seen-for 90d | enclave-cli system revoke --org "Acme Ltd" -
   ```

For the full CLI reference, see [docs.enclave.io](https://docs.enclave.io).
