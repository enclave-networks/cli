"""Build command reference pages from the CLI catalogue and reviewed examples."""

import json
import os
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs" / "commands"
catalogue = json.loads(
    subprocess.check_output(
        ["dotnet", "run", "--no-build", "--project", str(ROOT / "src" / "Enclave.Cli"), "--", "commands"],
        cwd=ROOT,
        text=True,
    )
)["commands"]

examples = {
    "login": "enclave-cli login --token-stdin < ~/.secrets/enclave-token",
    "logout": "enclave-cli logout",
    "status": "enclave-cli status --org 'Acme Ltd'",
    "org list": "enclave-cli org list",
    "org use": "enclave-cli org use 'Acme Ltd'",
    "org show": "enclave-cli org show --org 'Acme Ltd'",
    "org update": "enclave-cli org update --org 'Acme Ltd' --website https://acme.example",
    "org list-users": "enclave-cli org list-users --org 'Acme Ltd'",
    "org remove-user": "enclave-cli org remove-user former@acme.example --org 'Acme Ltd' --dry-run",
    "org list-invites": "enclave-cli org list-invites --org 'Acme Ltd'",
    "org invite": "enclave-cli org invite new@acme.example --org 'Acme Ltd'",
    "org cancel-invite": "enclave-cli org cancel-invite new@acme.example --org 'Acme Ltd'",
    "partner use": "enclave-cli partner use --id 8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6",
    "partner customer list": "enclave-cli partner customer list",
    "partner customer show": "enclave-cli partner customer show Initech",
    "partner customer create": "enclave-cli partner customer create Initech --owner it@initech.example --domain initech.example --systems 20 --gateways 1",
    "partner customer update": "enclave-cli partner customer update Initech --systems 25 --gateways 2",
    "partner customer convert": "enclave-cli partner customer convert Initech --billing-months 12",
    "partner customer list-admins": "enclave-cli partner customer list-admins Initech",
    "partner customer add-admin": "enclave-cli partner customer add-admin Initech --user-id 8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6",
    "partner customer remove-admin": "enclave-cli partner customer remove-admin Initech --user admin@initech.example",
    "partner customer list-invites": "enclave-cli partner customer list-invites Initech",
    "partner customer invite": "enclave-cli partner customer invite Initech --email admin@initech.example",
    "partner customer cancel-invite": "enclave-cli partner customer cancel-invite Initech --email admin@initech.example",
    "partner customer enable-auto-sync": "enclave-cli partner customer enable-auto-sync Initech",
    "partner customer disable-auto-sync": "enclave-cli partner customer disable-auto-sync Initech",
    "system list": "enclave-cli system list --state disconnected --not-seen-for 90d",
    "system show": "enclave-cli system show ABCDE",
    "system update": "enclave-cli system update ABCDE --add-tags office",
    "system approve": "enclave-cli system list --pending | enclave-cli system approve -",
    "system decline": "enclave-cli system decline ABCDE --dry-run",
    "system enable": "enclave-cli system enable ABCDE --for 8h --then revoke",
    "system disable": "enclave-cli system disable ABCDE FGHIJ",
    "system revoke": "enclave-cli system list --not-seen-for 90d | enclave-cli system revoke - --dry-run",
    "key list": "enclave-cli key list --state disabled",
    "key show": "enclave-cli key show 'Leeds office'",
    "key create": "enclave-cli key create 'Leeds office' --tags leeds,office --uses 20",
    "key update": "enclave-cli key update 'Leeds office' --add-tags production",
    "key enable": "enclave-cli key enable 'Leeds office' --for 8h",
    "key disable": "enclave-cli key disable 'Leeds office'",
    "key delete": "enclave-cli key delete 'Leeds office' --dry-run",
    "policy list": "enclave-cli policy list --tag laptops --state enabled",
    "policy show": "enclave-cli policy show 'laptops to file servers'",
    "policy create": "enclave-cli policy create 'laptops to file servers' --senders laptops --receivers file-servers --acl tcp:445",
    "policy update": "enclave-cli policy update 'laptops to file servers' --set-acl tcp:445 --set-acl tcp:139",
    "policy enable": "enclave-cli policy enable contractors --for 8h",
    "policy disable": "enclave-cli policy disable contractors",
    "policy delete": "enclave-cli policy delete contractors --dry-run",
    "tag list": "enclave-cli tag list --filter office",
    "tag show": "enclave-cli tag show office",
    "tag set": "enclave-cli tag set office --colour '#336699'",
    "tag delete": "enclave-cli tag delete office --dry-run",
    "dns show": "enclave-cli dns show",
    "dns list-zones": "enclave-cli dns list-zones",
    "dns show-zone": "enclave-cli dns show-zone internal",
    "dns create-zone": "enclave-cli dns create-zone internal --auto-dns-tags servers",
    "dns update-zone": "enclave-cli dns update-zone internal --set-auto-dns-tags servers,gateways",
    "dns delete-zone": "enclave-cli dns delete-zone internal --dry-run",
    "dns list-hostnames": "enclave-cli dns list-hostnames --zone internal",
    "dns show-hostname": "enclave-cli dns show-hostname db.internal",
    "dns create-hostname": "enclave-cli dns create-hostname db.internal --systems ABCDE,FGHIJ",
    "dns update-hostname": "enclave-cli dns update-hostname db.internal --add-tags database",
    "dns delete-hostname": "enclave-cli dns delete-hostname db.internal --dry-run",
    "trust list": "enclave-cli trust list --type public-ip",
    "trust show": "enclave-cli trust show 'office IPs'",
    "trust create": "enclave-cli trust create 'office IPs' --allow-ip '203.0.113.0/24=Office'",
    "trust update": "enclave-cli trust update 'office IPs' --set-allow-ip '203.0.113.0/24=Office'",
    "trust delete": "enclave-cli trust delete 'office IPs' --dry-run",
    "log": "enclave-cli log --since 24h --level warning,error",
    "commands": "enclave-cli commands system list --search-keys",
}

notes = {
    "login": "The token comes from standard input with `--token-stdin`, or from `ENCLAVE_TOKEN`. The command checks it before saving `~/.enclave/credentials.json`. If the token sees exactly 1 organisation, that organisation becomes the default.",
    "logout": "This deletes the saved credentials file. The token remains valid until it is revoked in the portal. Other tools that use `Enclave.Sdk.Api` read the same file.",
    "status": "The output names the token source and the selected organisation and partner. It also shows the token's role in the organisation. Use it to check context before a change.",
    "org use": "The default is saved in `~/.enclave/cli.json`. Use `--id <orgId>` when a name is ambiguous. A command's `--org` or `--org-id` overrides the default.",
    "partner use": "Get the partner ID from the partner portal. The default is saved in `~/.enclave/cli.json`; `--partner-id` overrides it for one command.",
    "partner customer create": "Set the owner and domain when creating the customer. `--systems` and `--gateways` set the licensed counts; `--auto-sync` keeps partner staff as customer admins.",
    "partner customer convert": "`--billing-months` is required. Accepted values are `1`, `12`, `24` and `36`.",
    "partner customer add-admin": "`--user-id` is the partner user's account ID, shown in the partner portal.",
    "system list": "Use `--pending` to list systems waiting for approval. `--not-seen-for` selects disconnected systems whose last seen time, or enrolment time if never seen, is older than the duration. List output can feed bulk commands through `-`.",
    "system show": "Add `--pending` for a system waiting for approval. System IDs contain letters and digits; the CLI reads them in upper case.",
    "system update": "`--set-tags` replaces tags; `--add-tags` and `--remove-tags` edit them. `--enable-gateway-for` replaces user-entered gateway routes. Add `--pending` to edit a waiting system; gateway options apply only to enrolled systems.",
    "system approve": "The input must come from `system list --pending` when using `-`. A bulk response reports `requested` and `affected`; a smaller affected count means some systems were unknown or already approved.",
    "system decline": "This removes waiting systems. Use `system list --pending` before passing its output as `-`.",
    "system enable": "`--for` or `--until` enables 1 system temporarily. `--then disable` is the default; `--then revoke` removes it when the time expires.",
    "system revoke": "Revoking removes a system from the organisation. `system list --not-seen-for 90d` selects disconnected systems for a cleanup pipeline.",
    "key list": "The output includes each key's secret. Handle it as a credential. `--state disabled` includes disabled keys without `--include-disabled`.",
    "key show": "The output includes the key's secret. Use `--id <id>` to avoid a name lookup.",
    "key create": "General-purpose keys require manual approval and have unlimited uses unless flags change those settings. `--ephemeral` creates a key with automatic approval, unlimited uses and a 30-minute disconnected-system retention. The output includes the key's secret.",
    "key update": "`--set-tags` replaces all tags. `--add-tags` and `--remove-tags` edit the existing list. `--set-allow-ip` replaces allowed ranges; repeat it for more than 1 range.",
    "key enable": "`--for` or `--until` enables 1 key temporarily. `--then disable` is the default; `--then delete` deletes it at expiry.",
    "policy create": "At least 1 `--acl` is required. `--receivers` makes a system-to-system policy; `--gateway SYSTEM:ROUTE` makes a gateway policy. `--mode` and `--subnet-filter` apply to gateway policies. `--disabled` creates the policy without enabling it.",
    "policy update": "Each `--set-` option replaces its collection. Repeat `--set-acl` for every allowed protocol and port. `--set-active-hours \"\"` removes the hours restriction.",
    "policy enable": "`--for` or `--until` enables 1 policy temporarily. `--then disable` is the default; `--then delete` deletes it at expiry.",
    "tag set": "The command updates an existing tag or creates a permanent tag. Provide at least 1 change flag. `--trust` or `--trust-id` replaces the tag's trust requirements.",
    "dns create-hostname": "Give the full hostname. The CLI selects the longest matching DNS zone. `--tags` and `--systems` choose what the hostname resolves to.",
    "dns update-hostname": "`--name` renames the full hostname within the same zone. `--set-tags` and `--set-systems` replace their lists; `--add-tags` and `--remove-tags` edit tags.",
    "trust create": "Use `--authority` for a sign-in requirement. Use `--allow-ip`, `--block-ip`, `--allow-country` or `--block-country` for a public IP requirement. Repeat a condition flag to add entries.",
    "trust update": "Each `--set-` option replaces conditions of its own kind. Other kinds stay as they are. To change an authority, create a new requirement and delete the old one.",
    "log": "The default is the newest 100 entries. `--since` reads back to a time; `--limit` caps the result. `--user`, `--level` and `--filter` filter matching entries.",
    "commands": "With no command words, this prints every command. Give words to narrow the result. `--search-keys` works with `system list`, `key list`, `policy list` and `tag list`; it fetches filter keys from the API. `--pending` selects the waiting-system keys for `system list`. Organisation options apply only with `--search-keys`.",
}

option_meanings = {
    "--id": "Select the item by ID.",
    "--org": "Use this organisation by name.",
    "--org-id": "Use this organisation by ID.",
    "--partner-id": "Use this partner by ID.",
    "--dry-run": "Print the API request without sending the change.",
    "--filter": "Search by text or the API's search syntax.",
    "--sort": "Sort the matching items.",
    "--pending": "Use systems waiting for approval.",
    "--for": "Keep the change for a duration such as `8h`.",
    "--until": "Keep the change until this time.",
    "--then": "Choose what happens at expiry.",
    "--notes": "Set notes; use an empty string to clear them on update.",
    "--token-stdin": "Read the token from standard input.",
    "--search-keys": "Fetch the list command's filter keys from the API.",
    "--billing-months": "Set the committed billing period in months.",
    "--user-id": "Select the account by ID.",
    "--user": "Select the user by email address.",
    "--email": "Select the invite by email address.",
    "--key-id": "Filter by enrolment key ID.",
    "--zone-id": "Filter by DNS zone ID.",
    "--trust-id": "Select trust requirements by ID.",
    "--colour": "Set the tag's hex colour.",
    "--acl": "Allow a protocol and optional port or range; repeat for more rules.",
    "--gateway": "Use a gateway system and route; repeat for more gateways.",
    "--mode": "Choose how a gateway is selected.",
    "--subnet-filter": "Limit addresses reached through a gateway.",
    "--active-hours": "Limit when the policy is active.",
    "--authority": "Choose the sign-in authority.",
    "--allow-ip": "Allow an IP range; repeat for more ranges.",
    "--block-ip": "Block an IP range; repeat for more ranges.",
    "--allow-country": "Allow a country code; repeat for more countries.",
    "--block-country": "Block a country code; repeat for more countries.",
    "--name": "Set the item's name.",
    "--description": "Set the item's description.",
    "--website": "Set the organisation's website.",
    "--phone": "Set the organisation's phone number.",
    "--owner": "Set the customer's owner email address.",
    "--domain": "Set the customer's domain.",
    "--contact": "Set the customer's contact name.",
    "--systems": "Set the licensed system count, or the hostname's system IDs.",
    "--gateways": "Set the licensed gateway count.",
    "--industry-discount": "Request an industry discount.",
    "--hard-limit": "Apply a hard licence limit.",
    "--auto-sync": "Keep partner staff as customer admins.",
    "--tag": "Filter by tags; a comma list requires every tag.",
    "--tags": "Set the initial tags as a comma list.",
    "--state": "Filter by state.",
    "--type": "Filter by type.",
    "--os": "Filter by operating system.",
    "--key": "Filter by enrolment key description.",
    "--not-seen-for": "Select disconnected systems older than this duration.",
    "--waiting-for": "Select pending systems older than this duration.",
    "--include-disabled": "Include disabled items.",
    "--enable-gateway-for": "Replace user-entered gateway routes; repeat for more subnets.",
    "--disable-gateway": "Stop using the system as a gateway.",
    "--auto-approve": "Approve enrolment automatically.",
    "--require-approval": "Require manual approval for enrolment.",
    "--ephemeral": "Create an ephemeral enrolment key.",
    "--uses": "Set how many enrolments remain.",
    "--keep-disconnected": "Keep disconnected ephemeral systems for this duration.",
    "--approval": "Filter by approval mode.",
    "--senders": "Set sender tags as a comma list.",
    "--receivers": "Set receiver tags as a comma list.",
    "--trust": "Select trust requirements by description.",
    "--disabled": "Create the policy disabled.",
    "--zone": "Filter hostnames by zone name.",
    "--auto-dns-tags": "Set tags used for automatic DNS entries.",
    "--tenant": "Set the Azure tenant ID.",
    "--authority-uri": "Set the authority URL.",
    "--client-id": "Set the identity provider's client ID.",
    "--audience": "Set the expected audience.",
    "--claim": "Require a claim and value; repeat for more claims.",
    "--limit": "Limit the number of entries printed.",
    "--since": "Start at this past time or duration.",
    "--level": "Filter by log level; comma lists are accepted.",
}


def option_description(name):
    if name in option_meanings:
        return option_meanings[name]
    if name.startswith("--set-"):
        return "Replace the " + name[6:].replace("-", " ") + " setting or list."
    if name.startswith("--add-"):
        return "Add " + name[6:].replace("-", " ") + "."
    if name.startswith("--remove-"):
        return "Remove " + name[9:].replace("-", " ") + "."
    if name.startswith("--no-"):
        return "Turn off " + name[5:].replace("-", " ") + "."
    return "Set " + name[2:].replace("-", " ") + "."


def output_description(command):
    if command == "commands":
        return "JSON with command definitions and exit codes."
    if command == "status":
        return "JSON with the token source, organisation, partner and role."
    if command == "logout":
        return "JSON with the credentials path and a `deleted` boolean."
    if command == "partner use":
        return "JSON with the selected partner ID."
    if command.endswith(" use"):
        return "The selected organisation as JSON."
    if command.endswith(" list") or " list-" in command or command == "login":
        return "A JSON list with `kind`, `items` and `total`."
    if command == "log":
        return "A JSON list of activity entries, newest first."
    if command.endswith((" approve", " decline", " disable", " revoke", " delete", " delete-hostname")) and command != "dns delete-zone":
        return "A JSON result with `requested` and `affected`."
    if command.endswith(" enable"):
        return "A JSON result with `requested` and `affected`; a timed enable prints the updated item."
    return "The resulting item as JSON."


def page_for(entry, path):
    command = entry["command"]
    title = f"`enclave-cli {command}`"
    args = " ".join(f"<{arg['name']}>" for arg in entry["arguments"])
    synopsis = f"enclave-cli {command}" + (f" {args}" if args else "") + (" [options]" if entry["options"] else "")
    if command == "commands":
        synopsis = "enclave-cli commands [<command words>...] [options]"
    description = notes.get(command, entry["description"])
    options = entry["options"]
    lines = [f"# {title}", "", description, "", "## Synopsis", "", "```text", synopsis, "```", "", "## Example", "", "```sh", examples[command], "```", ""]
    if args and "--id" in [option["name"] for option in options]:
        lines += ["Use the name argument or `--id <id>` to select the item.", ""]
    if args and "--org-id" in [option["name"] for option in options] and command.startswith("partner customer "):
        lines += ["Use the customer name or `--org-id <orgId>` to select the customer.", ""]
    if options:
        lines += ["## Options", "", "| Option | Use |", "| --- | --- |"]
        for option in options:
            values = option.get("values")
            use = option_description(option["name"])
            if option["name"] == "--org-id" and command.startswith("partner customer "):
                use = "Select the customer by organisation ID."
            if option["name"] == "--id" and command == "partner use":
                use = "Set the default partner by ID."
            if option["name"] == "--gateway" and command == "system list":
                use = "List gateway systems only."
            if option["name"] == "--systems" and command == "dns create-hostname":
                use = "Set system IDs the hostname answers for."
            if option["name"] == "--until" and command == "log":
                use = "Leave out entries newer than this time."
            if values:
                use += " Values: " + ", ".join(f"`{value}`" for value in values) + "."
            lines.append(f"| `{option['name']}` | {use} |")
        lines.append("")
    lines += ["## Output", "", output_description(command), ""]
    if "--dry-run" in [option["name"] for option in options]:
        lines += ["Add `--dry-run` to inspect the request before making a change.", ""]
    index_link = Path(os.path.relpath(DOCS / "README.md", path.parent)).as_posix()
    lines += [f"Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code]({index_link}#exit-codes).", "", f"See the [command index]({index_link}) for shared input rules.", ""]
    return "\n".join(lines)


groups = {}
for entry in catalogue:
    command = entry["command"]
    parts = command.split()
    path = DOCS.joinpath(*parts[:-1], parts[-1] + ".md")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(page_for(entry, path), encoding="utf-8")
    group = " ".join(parts[:-1]) or "General"
    groups.setdefault(group, []).append((command, path.relative_to(DOCS).as_posix(), entry["description"].split(". ")[0].rstrip(".")))

index = ["# Command reference", "", "`enclave-cli` commands follow `enclave-cli <noun> <verb> [arguments] [options]`. Each page covers one command. Use `enclave-cli commands <command words>` for a machine-readable description of arguments, options and allowed values.", "", "## Shared rules", "", "- Select an organisation with `--org <name>` or `--org-id <id>`, or save one with [`org use`](org/use.md). Partner commands use [`partner use`](partner/use.md) or `--partner-id`.", "- Use `--id` to select a named item by ID. Systems use IDs as arguments. A bulk command also accepts `-` to read matching list output from standard input.", "- Changes take effect when given. Use `--dry-run` on API changes to inspect requests. Commands that change local files do not take it.", "- Durations use minutes, hours or days: `30m`, `8h`, `7d`. Times without a zone use the machine's local time zone.", "- `--set-` replaces a list; `--add-` and `--remove-` edit it. An empty string clears a setting only where the command accepts an empty value.", "- Every successful command prints JSON to standard output. Errors print JSON to standard error. The output includes a `trace` when the command took a step worth tracing.", "", "## Exit codes", "", "| Code | Meaning |", "| --- | --- |", "| 0 | Success |", "| 1 | API error or unimplemented command |", "| 2 | Invalid argument, missing organisation or missing partner |", "| 3 | Missing or invalid token |", "| 4 | Forbidden by the API |", "| 5 | Item not found |", "| 6 | Network error, timeout, rate limit or server error |", ""]
for group, commands in groups.items():
    index += [f"## {group.capitalize()}", "", "| Command | Purpose |", "| --- | --- |"]
    for command, link, purpose in commands:
        index.append(f"| [`enclave-cli {command}`]({link}) | {purpose}. |")
    index.append("")
index += ["For complete behaviour and defaults, see the [CLI specification](../../spec/cli-surface.md).", ""]
(DOCS / "README.md").write_text("\n".join(index), encoding="utf-8")
print(f"Wrote {len(catalogue)} command pages and the index.")
