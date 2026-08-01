# NTFS Deleted Files Viewer

**Exactly what NTFS says was deleted — shown in a table ordinary users can understand.**

A portable, read-only Windows utility for inspecting the NTFS USN change journal and displaying retained file-deletion records in a searchable, sortable table.

![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white)
![Version](https://img.shields.io/badge/version-0.1.1-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Status](https://img.shields.io/badge/status-early%20release-orange)


> [!IMPORTANT]
> This is an **investigation and timeline tool**, not file-recovery software. It shows deletion events retained in the NTFS journal. It does not restore deleted file contents.

## Why this exists

After an accidental `Shift+Delete`, Windows does not provide a clear list of what was actually deleted. Recovery programs may immediately focus on recoverable data, while the NTFS USN journal can answer a different and often more urgent question:

> **What does NTFS say was deleted, and when?**

NTFS Deleted Files Viewer reads that journal and presents matching `FILE_DELETE` records in a table—without requiring users to parse `fsutil` output manually or import it into a spreadsheet.

## Features

- Reads an existing NTFS USN change journal
- Lists every retained record containing `USN_REASON_FILE_DELETE`
- Shows deletion time, filename, type, reason flags, file IDs, attributes, USN and record version
- Sortable and searchable native Windows table
- Date-and-time filtering
- **Incident check** for a suspected deletion time with a configurable ± minute window
- Timeline-coverage verification using records before and after the selected window
- Optional best-known parent-path resolution
- UTF-8 CSV export of visible rows
- Copy selected cells with headers
- Native Windows API engine
- `fsutil` CSV compatibility engine
- Scan cancellation and journal-wrap handling
- No installer, NuGet package, browser runtime or third-party library
- Runs locally and does not upload journal data

## Screenshot


## Requirements

- Windows 10 or Windows 11
- A local, ready **NTFS** volume
- An active USN change journal on that volume
- Administrator access
- .NET Framework 4.8

The program is Windows-only because it communicates directly with NTFS volume-control APIs.

## Quick start

### Option A: Build from the included source

1. Download or clone the repository on a drive **other than the volume being investigated** (**CRITICAL!!!** You can overwrite the deleted files and make their recovery impossible when saved to the same volume!!!).
2. Extract (again, on a different drive than the volume being investigated).
3. Double-click **`Build and Run.cmd`**.
4. Accept the administrator prompt.
5. Select an NTFS drive.
6. Leave **Native Windows API (recommended)** selected.
7. Leave **Read all record types to verify timeline coverage** enabled.
8. Click **Scan journal**.

The build script uses the C# compiler included with .NET Framework and creates:

```text
NTFS Deleted Files Viewer.exe
```

After the first successful build, use **`Run.cmd`** or start the EXE directly.

### Option B: Download a compiled release

Download the latest ZIP from the repository’s **Releases** page, extract it, and run the included executable.

> [!NOTE]
> Release executables may be unsigned. Windows SmartScreen can therefore display a warning. Verify that the file came from the official repository and compare its SHA-256 checksum with the value published alongside the release.



## Using the application

### Scan a journal

Choose the target NTFS volume and click **Scan journal**.

For the strongest timeline conclusion, keep these options enabled:

- **Read all record types to verify timeline coverage**
- **Resolve surviving parent folders**

Reading all record types is slower, but it allows the application to determine whether the retained journal actually spans the time you are investigating.

### Filter the table

Use the search box and date/time controls to narrow the visible results. The table can be sorted by clicking a column header.

The available fields include:

| Column          | Meaning                                                     |
| --------------- | ----------------------------------------------------------- |
| Deleted at      | Timestamp stored in the USN record                          |
| Name            | Filename recorded by NTFS                                   |
| Best-known path | Filename combined with the currently resolvable parent path |
| Type            | File or directory                                           |
| Path status     | Whether parent-path resolution succeeded                    |
| Reason          | Human-readable USN reason flags                             |
| Reason code     | Numeric reason bitmask                                      |
| File ID         | NTFS file-reference identifier                              |
| Parent file ID  | NTFS identifier of the recorded parent                      |
| USN             | Update Sequence Number                                      |
| Record version  | Parsed USN record version                                   |
| Attributes      | Recorded file attributes                                    |

### Check a suspected incident time

The **Incident check** is intended for situations such as an accidental keypress or interrupted deletion:

1. Scan with timeline verification enabled.
2. Enter the approximate incident time.
3. Select a window, such as ±5 minutes.
4. Click **Check this window**.

The result distinguishes between:

- no deletion records found **inside a journal-covered window**;
- one or more deletion records found in the window;
- a window that cannot be verified because retained journal coverage is incomplete or unknown.

A zero-result conclusion is reassuring only when the requested interval lies fully between the earliest and latest timestamps observed during the scan.

### Export results

Click **Export visible CSV** to save the currently filtered table as UTF-8 CSV.

When recovery might still be necessary, save the CSV to a different physical drive. Writing new data to the affected volume can overwrite space formerly occupied by deleted files.

## Scan engines

### Native Windows API — recommended

The primary engine opens the selected volume and uses Windows volume-control functions directly:

- `FSCTL_QUERY_USN_JOURNAL`
- `FSCTL_READ_USN_JOURNAL`
- `OpenFileById` for optional parent resolution

This avoids console localization, CSV formatting and filename-encoding problems.

The program tests the `USN_REASON_FILE_DELETE` bit (`0x00000200`) as a flag rather than expecting one exact reason value. Deletion can be combined with other flags, including `CLOSE`.

### `fsutil` CSV compatibility mode

The fallback engine runs the Windows command:

```cmd
fsutil usn readjournal D: startusn=0 csv
```

The output is parsed in memory and shown in the same table. No temporary journal dump is created.

Use this mode when the native request is rejected by an unusual Windows, filesystem or storage configuration. The native engine remains preferable, particularly for non-ASCII filenames.

## What “best-known path” means

A USN deletion record contains the deleted item’s filename and its parent file ID. It does **not** contain a guaranteed historical full path.

The application asks Windows whether the recorded parent directory ID still exists:

- **Parent resolves now** — Windows currently resolves the parent ID, and the app combines that current path with the recorded filename.
- **Parent unavailable** — the parent directory can no longer be opened by that ID.
- **Not requested** — path resolution was disabled.
- **Unresolved** — resolution was attempted but did not produce a path.

A parent directory may have been renamed after the deletion. For this reason, the displayed value is labelled a **best-known path**, not a guaranteed original path.

The filename, timestamp, IDs, reason bits, attributes and USN come from the journal record itself.

## Safety and read-only design

The application contains no feature for creating, deleting, disabling, resetting or resizing a USN journal.

It does not:

- recover deleted file contents;
- restore files;
- modify journal records;
- intentionally write to the examined drive;
- identify which user or application initiated a deletion;
- prove that deleted file data remains recoverable.

The application or an exported CSV can still write to the examined volume if you deliberately store them there. Keep the program and its output on another drive during a recovery-sensitive investigation.

## Important limitations

The USN journal is a finite change log, not permanent history.

- Older records may disappear when the journal wraps.
- A deleted, recreated or inactive journal cannot reveal earlier activity.
- A deletion event does not mean the file’s content is still recoverable.
- Temporary files and routine application cleanup also generate deletion records.
- Exact historical paths may be unavailable.
- Unsupported USN record versions are skipped.
- Timeline coverage is marked unverified when unsupported or malformed records prevent a complete scan.
- Very large journals can take time to scan when full coverage verification is enabled.
- The journal records filesystem changes, not the identity or intent of the person or program responsible.

Do not treat the absence of a result as proof unless the application confirms that the relevant time window lies inside the observed journal timeline.

## Project files

```text
.
├── Program.cs
├── Build and Run.cmd
├── Run.cmd
├── README.md
├── CHANGELOG.md
└── LICENSE.txt
```

## Building manually

`Build and Run.cmd` looks for the .NET Framework compiler in:

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

and falls back to:

```text
%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
```

Equivalent manual build command:

```bat
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" ^
  /nologo ^
  /target:winexe ^
  /platform:anycpu ^
  /optimize+ ^
  /warn:4 ^
  /codepage:65001 ^
  /out:"NTFS Deleted Files Viewer.exe" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Data.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "Program.cs"
```

No package restore is required.

## Troubleshooting

### The compiler was not found

Enable or install .NET Framework 4.8, then run `Build and Run.cmd` again.

### Windows asks for administrator access

Direct volume access requires elevation. Accept the User Account Control prompt. Cancelling it closes the application.

### The selected drive is missing

The drive must be local, ready and formatted as NTFS. Click **Refresh** after connecting or waking an external drive.

### The USN journal is unavailable

The selected volume may not have an active journal, may not be NTFS, or may reject the request through its storage interface. Try the `fsutil` compatibility engine for diagnosis.

The application deliberately does not create a journal automatically, because doing so would not recreate historical records.

### No deletion records appear

Possible explanations include:

- no retained deletion records match the filter;
- the journal’s older records have wrapped;
- the selected date filter excludes them;
- the wrong drive was selected;
- the journal was recreated after the event;
- the scan did not cover the relevant period.

Review the timeline-coverage message before drawing a conclusion.

### Some paths are unresolved

This is expected when the recorded parent directory no longer exists or cannot be opened by its NTFS identifier. The filename and other record fields may still be valid.

### Native scanning fails

Allow the application to retry with the `fsutil` CSV compatibility engine. When reporting a bug, include:

- Windows version
- drive type and connection method
- filesystem
- selected engine
- exact error message
- whether `fsutil usn queryjournal X:` works in an elevated Command Prompt

Do not publish private filenames or paths unless they are necessary and safely redacted.

## Roadmap

Potential future improvements:

- richer historical path reconstruction
- signed release binaries
- packaged installer and portable release
- saved filters and scan sessions
- JSON export
- command-line mode
- extension and directory grouping
- faster indexing for extremely large journals
- automated Windows build and release workflow
- additional USN record-version support

See [`CHANGELOG.md`](CHANGELOG.md) for completed changes.

## Contributing

Bug reports, test results and pull requests are welcome.

Good issue reports include exact reproduction steps and the complete error message. For storage-specific problems, mention whether the volume is internal SATA/NVMe, USB, Thunderbolt, a dock/enclosure, virtual storage or another configuration.

Please avoid attaching real journal exports containing sensitive filenames. Create a small reproducible test case when possible.

## Security and privacy

All journal processing is performed locally. The source contains no telemetry or upload feature.

Because the application requires administrator rights and reads raw volume metadata, download builds only from the official repository. Source builds are encouraged for users who want to inspect exactly what is executed.

Security concerns should be reported privately to the repository owner rather than posted with exploit details in a public issue.

## Disclaimer

This software is provided for informational and investigative use. Filesystem journals can be incomplete, overwritten, malformed or unavailable. The program makes no guarantee that all deletions will be found, that paths are historically exact, or that deleted data can be recovered.

Always preserve important storage media and consult a qualified data-recovery specialist when the data is valuable or irreplaceable.

## License

Released under the [MIT License](LICENSE.txt).

---

Made to answer one frightening question clearly: What the SHIFT+Delte deleted???

**“Did NTFS actually delete anything?”**

