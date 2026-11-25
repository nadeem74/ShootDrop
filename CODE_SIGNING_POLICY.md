# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

> **Status:** applied for, not yet approved. Until approval, the Windows download is unsigned and Windows shows
> "Windows protected your PC": click **More info → Run anyway**. Once approved, every Windows release is signed.

## What is signed

- `ShootDrop-Windows.exe`, attached to each [GitHub Release](https://github.com/nadeem74/ShootDrop/releases).

Signed files are built only from the source code in this repository, by the
[release workflow](.github/workflows/release.yml) on GitHub-hosted runners. Nothing built elsewhere is signed.

The macOS builds are not covered by this policy. They are not notarised yet (that needs a paid Apple Developer ID).

## Team roles

| Role | Who | What they do |
| --- | --- | --- |
| Authors | [Nadeem Ahmed (@nadeem74)](https://github.com/nadeem74) | Can change the code without extra review |
| Reviewers | [Nadeem Ahmed (@nadeem74)](https://github.com/nadeem74) | Review every pull request from other contributors before it is merged |
| Approvers | [Nadeem Ahmed (@nadeem74)](https://github.com/nadeem74) | Approve each signing request in SignPath, one release at a time |

All team members use multi-factor authentication for GitHub and SignPath. The `main` branch is protected:
changes go through pull requests and must pass the build and tests first.

## Privacy

ShootDrop does not send any information to other networked systems unless the user asks it to.
It reads your camera cards and writes to the folder you choose, and that's all. There is no telemetry,
no account and no analytics. Settings stay on your computer
(`%AppData%\ShootDrop` on Windows, `~/Library/Application Support/ShootDrop` on macOS).

## Uninstall

The Windows download is a single portable file. To remove ShootDrop, delete `ShootDrop-Windows.exe`,
and delete the `%AppData%\ShootDrop` folder to remove your settings too.

## Reporting a problem

If you think a signed ShootDrop file is not what it should be, please open an issue or follow [SECURITY.md](SECURITY.md).
