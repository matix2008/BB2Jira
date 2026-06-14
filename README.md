# BB2Jira

`BB2Jira` is a .NET 8 console utility that converts a Bitbucket issue export
(`db-2.0.json`) into files that can be imported into an existing Jira project.

It produces two artifacts:

- a **mapping file** `map.json` (run with the `-m` key);
- an **import file** `import.csv` (run with the `-c` key).

`import.csv` is intended for importing issues and bugs from Bitbucket into a Jira project.
Jira generates new issue keys during import, while the original Bitbucket number is
preserved in a dedicated field, `Bitbucket Issue ID`.

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

```bash
dotnet build
```

## Run

```bash
# Generate map.json from db-2.0.json
BB2Jira -m [-i db-2.0.json] [-o map.json]

# Generate import.csv from db-2.0.json + map.json
BB2Jira -c -i db-2.0.json -m map.json -o import.csv

# Validate an existing import.csv
BB2Jira -k [-o import.csv] [-i db-2.0.json] [-m map.json]

# Update Jira issues via API
BB2Jira -u [-o import.csv] [-m map.json]
```

When run from source you can use:

```bash
dotnet run --project BB2Jira -- -m -i db-2.0.json -o map.json
```

---

## Command-line options

| Key            | Description                                                                          |
| -------------- | ------------------------------------------------------------------------------------ |
| `-m`, `--map`  | Generate `map.json` (without `-c`/`-k`/`-u`); path to `map.json` (with `-c`/`-k`/`-u`) |
| `-c`, `--csv`  | Generate `import.csv`                                                                 |
| `-k`, `--check`| Validate an existing `import.csv`                                                     |
| `-u`, `--update`| Update Jira issues via API                                                           |
| `-i`, `--input`| Path to the Bitbucket export file (`db-2.0.json`)                                    |
| `-o`, `--output`| Path to the result (`map.json` or `import.csv`)                                     |
| `-v`, `--verbose`| Show per-issue diagnostics on the console                                            |
| `-h`, `--help` | Show help                                                                             |

The utility prints a version and copyright banner on every launch, including when
showing help.

### Defaults

| Mode | Input(s)                      | Output       |
| ---- | ----------------------------- | ------------ |
| `-m` | `db-2.0.json`                 | `map.json`   |
| `-c` | `db-2.0.json` + `map.json`    | `import.csv` |
| `-k` | `import.csv` (+ optional `db-2.0.json` and `map.json`) | `import.csv` (checked) |
| `-u` | `import.csv` + `map.json` (with `jira` section filled) | Jira updated in place |

The `-m` key is overloaded:

- **without `-c`** it selects the `map.json` generation mode;
- **together with `-c`** it provides the path to an existing `map.json`;
- **together with `-k`** it provides the path to `map.json` for cross-reference validation;
- **together with `-u`** it provides the path to `map.json` containing the `jira` section.

---

## Jira update (`-u`)

The `-u` key reads `import.csv` and updates matching Jira issues via the REST API v3.
Logs are written to `import-update.log`. A resume file `import-update.progress` is
maintained so interrupted runs can continue from where they left off.

### What is updated

| Field | Condition |
|---|---|
| Status | When `updateStatus: true` in `map.json` — applied via workflow transition |
| Comments | When `updateComments: true` — adds comments whose date is later than the latest existing Jira comment |

### How issues are matched

The utility searches Jira for issues whose `description` contains the Bitbucket issue URL
(`{bitbucketRepoUrl}/issues/{id}`). The Bitbucket issue number is extracted from that URL
and matched against the `Bitbucket Issue ID` column in `import.csv`.

### Comment format

Comments are stored in Jira as plain-text ADF paragraphs:
```
2024-01-15 10:00:00 | John Doe | Original comment text
```

### `jira` section in `map.json`

Generated automatically by `-m` with placeholder values. Fill in before running `-u`:

```json
"jira": {
  "baseUrl": "https://yoursite.atlassian.net",
  "projectKey": "PROJ",
  "email": "user@example.com",
  "apiToken": "your_api_token_here",
  "bitbucketRepoUrl": "https://bitbucket.org/yourorg/yourrepo",
  "updateStatus": true,
  "updateComments": true
}
```

### Exit codes

| Code | Meaning |
|---|---|
| `0` | All issues processed successfully |
| `1` | Fatal error (CSV not found, Jira unreachable, missing config) |
| `2` | Partial errors (some transitions failed; processing continued) |

---

## Validation (`-k`)

The `-k` key checks an existing `import.csv` and writes a log to `import-check.log`
(placed next to the validated file).

### Checks performed

| # | Check | Severity | Stops? |
|---|-------|----------|--------|
| 1 | File exists and is readable UTF-8 | Error | Yes |
| 2 | CSV is syntactically valid (RFC 4180, balanced quotes, uniform column count) | Error | Yes |
| 3 | Header row contains all required columns in the correct order | Error | No* |
| 4 | Required fields (`Issue Type`, `Summary`, `Status`) are not empty | Error | No |
| 5 | `Created` / `Updated` match format `yyyy-MM-dd HH:mm:ss` | Warning | No |
| 6 | `Bitbucket Issue ID` values are unique | Warning | No |
| 7 | *(when `-i` is resolvable)* Every issue from the export has a row in the CSV | Warning | No |
| 8 | *(when `-m` is resolvable)* `Issue Type`, `Status`, `Priority`, `Fix Version/s`, `Bitbucket Milestone` values match mapped values in `map.json` (case-sensitive) | Warning | No |
| 9 | No duplicate column names in the header | Error | No |
| 10 | No columns outside the allowed set (`BaseColumns` + `Comment*`) | Warning | No |
| 11 | No hidden or control characters in column names (`BOM`, `\t`, `\r`, `\n`, `\u00A0`, zero-width chars) | Error | No |

\* Checks 9, 10, and 11 always run on the same header row. If check 3 fails, data-row checks (4–8) are skipped but the header checks complete first.

### Exit codes

| Code | Meaning |
|------|---------|
| `0`  | Validation passed (warnings may still be present) |
| `1`  | Unexpected error (file I/O, JSON parse failure) |
| `2`  | Validation failed (one or more errors) |

---

## `map.json` format

```json
{
  "kind": {
    "bug": "Bug",
    "task": "Task",
    "enhancement": "Task",
    "proposal": "Task"
  },

  "status": {
    "new": "Backlog",
    "open": "In Development",
    "resolved": "Ready for Release",
    "on hold": "Planned",
    "invalid": "Canceled",
    "duplicate": "Canceled",
    "wontfix": "Canceled"
  },

  "priority": {
    "trivial": "Lowest",
    "minor": "Low",
    "major": "Medium",
    "critical": "High",
    "blocker": "Highest"
  },

  "users": {
    "557058:xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx": {
      "bitbucketDisplayName": "User Name",
      "jiraAccountId": "",
      "jiraEmail": "",
      "jiraDisplayName": "User Name"
    }
  },

  "milestone": {
    "MVP-1": "MVP-1",
    "MVP-2": "MVP-2"
  },

  "version": {
    "1.0": "1.0",
    "1.1": "1.1"
  }
}
```

### `map.json` generation rules

The mapping is built from the values found in `db-2.0.json`.

#### `kind`

- Source: `issues[].kind`
- Each unique `kind` maps to a Jira issue type.
- Default mapping: `bug -> Bug`, `task -> Task`, `enhancement -> Task`, `proposal -> Task`.
- Unknown value: `<bitbucket kind> -> Task`.

#### `status`

- Source: `issues[].status`
- Each unique `status` maps to a Jira status.
- Default mapping: `new -> Backlog`, `open -> In Development`, `resolved -> Ready for Release`,
  `on hold -> Planned`, `invalid -> Canceled`, `duplicate -> Canceled`, `wontfix -> Canceled`.
- Unknown value: `<bitbucket status> -> Backlog`.

#### `priority`

- Source: `issues[].priority`
- Each unique `priority` maps to a Jira priority.
- Default mapping: `trivial -> Lowest`, `minor -> Low`, `major -> Medium`,
  `critical -> High`, `blocker -> Highest`.
- Unknown value: `<bitbucket priority> -> Medium`.

#### `users`

- Sources: `issues[].reporter`, `issues[].assignee`, `comments[].user`, `logs[].user`.
- The user key is selected in this order: `account_id`, then `display_name`.
- Each entry stores:
  - `bitbucketDisplayName` � the Bitbucket display name;
  - `jiraAccountId` � empty by default; fill it in manually to preserve comment and history authors;
  - `jiraEmail` � empty by default;
  - `jiraDisplayName` � defaults to `bitbucketDisplayName`.

#### `milestone`

- Sources: `milestones[].name`, `issues[].milestone`.
- Default: `<bitbucket milestone> -> <bitbucket milestone>`.

#### `version`

- Sources: `versions[].name`, `issues[].version`.
- Default: `<bitbucket version> -> <bitbucket version>`.

### General rules

1. Empty values are not added to `map.json`.
2. All keys are sorted alphabetically.
3. A value found in the `versions` or `milestones` reference is added even if it is not used by any issue.
4. A value used by an issue is added even if it is missing from the top-level reference.
5. On regeneration, manually edited values are **not** overwritten.
6. When `map.json` already exists, the utility:
   - adds newly found values;
   - preserves existing manual edits;
   - does not automatically remove old values.
7. `map.json` is used only to convert `db-2.0.json` into `import.csv`.
8. Jira project settings, custom field names, the project key, and Bitbucket issue URLs are **not** stored in `map.json`.

---

## `import.csv` format

One CSV row corresponds to one entry from `issues[]`. Only issues whose `kind` maps to
`Task` or `Bug` are included. Rows are sorted by ascending `issues[].id`.

### Encoding and formatting

- Encoding: `utf-8-sig` (UTF-8 with BOM).
- Delimiter: `,` (comma).
- Text values are escaped using standard CSV rules (RFC 4180).

### Columns

```csv
Issue Type,Summary,Description,Status,Priority,Reporter,Assignee,Created,Updated,Fix Version/s,Bitbucket Milestone,Bitbucket Issue ID,Comment
```

If an issue has several comments / history entries, the `Comment` column is repeated:

```csv
Issue Type,Summary,Description,...,Bitbucket Issue ID,Comment,Comment,Comment
```

### Column rules

| Column                | Rule                                                                                  |
| --------------------- | ------------------------------------------------------------------------------------- |
| `Issue Type`          | `map.kind[issues[].kind]`. If missing, the issue is skipped and logged.               |
| `Summary`             | `issues[].title`; if empty, `Bitbucket issue <id>`.                                   |
| `Description`         | `issues[].content` plus an import service block.                                      |
| `Status`              | `map.status[issues[].status]`. If missing, left empty and logged.                     |
| `Priority`            | `map.priority[issues[].priority]`; if missing, `Medium`.                              |
| `Reporter`            | `map.users[...].jiraAccountId` (falls back to `jiraEmail`). If unmapped, empty + log. |
| `Assignee`            | `map.users[...].jiraAccountId` (falls back to `jiraEmail`); empty if unmapped.        |
| `Created`             | `issues[].created_on`, formatted as `yyyy-MM-dd HH:mm:ss`.                            |
| `Updated`             | `issues[].updated_on`; if absent, equals `Created`.                                   |
| `Fix Version/s`       | `map.version[issues[].version]`; empty if absent.                                     |
| `Bitbucket Milestone` | `map.milestone[issues[].milestone]`; empty if absent.                                 |
| `Bitbucket Issue ID`  | `issues[].id`.                                                                         |
| `Comment`             | One column per comment / history event (see below).                                   |

The description service block appended to every issue:

```text
---

Imported from Bitbucket
Bitbucket Issue ID: <issues[].id>
```

### Comments and history

Comments (`comments[]`) and change history (`logs[]`) are merged into a single list of
events per issue, sorted by `created_on` ascending. Each event is written into its own
`Comment` column.

Comment format:

```text
<date>;<jiraUser>;<comment text>
```

If the comment author is not mapped:

```text
<date>;;[Original Bitbucket author: <display_name>]

<comment text>
```

History entry format:

```text
<date>;<jiraUser>;[Bitbucket history] <field>: <changed_from> -> <changed_to>
```

If the history author is not mapped:

```text
<date>;;[Bitbucket history]
Original Bitbucket author: <display_name>
<field>: <changed_from> -> <changed_to>
```

`jiraUser` is `map.users[...].jiraAccountId`, falling back to `jiraEmail`.

---

## Logging

During CSV generation the utility writes a log file (`import.log` for CSV mode,
`map.log` for map mode) and mirrors messages to the console. The log records:

1. number of processed issues;
2. number of exported rows;
3. number of comments;
4. number of history entries;
5. values missing from `map.json`;
6. users without `jiraAccountId` / `jiraEmail`;
7. empty required fields;
8. skipped issues, if any.

---

## Output files

After a successful CSV run the following files are produced:

```text
import.csv
import.log
```

---

## License

Licensed under the Apache License 2.0.

(C) Realant Ltd., 2026.
