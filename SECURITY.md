# Security

ShootDrop handles people's footage, so data-loss bugs are treated with the same urgency as security bugs.

## Reporting

- **Security issue** (for example, a way to make ShootDrop write outside the project folder or touch a card):
  please report it privately through GitHub's
  [private vulnerability reporting](https://github.com/nadeem74/ShootDrop/security/advisories/new),
  not a public issue.
- **Data-loss bug** (a file missing, corrupted or overwritten): open an issue with the `copy-report.txt`
  from the project. If you still have the card, don't format it.

You'll get a reply within a few days.

## Supported versions

Only the latest release gets fixes while ShootDrop is in 0.x.

## What ShootDrop does and doesn't do

- Opens cards read-only. Writes only inside the chosen projects folder and its own settings folder.
- No telemetry, no accounts, no network use in v0.1. The planned AI model (v0.2) is an optional download and runs offline.
