# 🤖 GitHub PR Export 🤖

[![MIT License](https://img.shields.io/github/license/matthetherington/GitHubPRExport?style=for-the-badge&labelColor=232925&color=0FBF3E)](https://choosealicense.com/licenses/mit/)

Exports all open and closed pull requests from a GitHub repository to a CSV containing:

- Title
- Author
- Date (the PR creation date in ISO 8601 format)
- Files Changed
- Lines Added
- Lines Removed
- Description (the PR body)

## Authentication

A token is optional for public repositories, but unauthenticated GitHub API requests have a low rate limit. A token is required for private repositories.

GitHub requires one additional API request per PR to obtain its changed-file, addition and deletion counts. Using a token is therefore strongly recommended even for public repositories with more than a few PRs.

Prefer a fine-grained personal access token restricted to the repository being exported, with:

- **Metadata: Read-only** (automatically selected by GitHub)
- **Pull requests: Read-only**

Store it in .NET user secrets under `GitHub:Token`:

```sh
dotnet user-secrets set "GitHub:Token" "YOUR_TOKEN" --project GitHubPRExport
```

Do not put the token in source code or commit it to the repository.

## Run

From the solution directory:

```sh
dotnet run --project GitHubPRExport -- <owner> <repository> [output.csv] [--author <username>]
```

For example:

```sh
dotnet run --project GitHubPRExport -- dotnet runtime runtime-prs.csv
```

If the output path is omitted, the file is named `<owner>-<repository>-pull-requests.csv` in the current directory.

To export only PRs opened by a particular GitHub user, add `--author`. Username matching is case-insensitive:

```sh
dotnet run --project GitHubPRExport -- dotnet runtime runtime-prs.csv --author github-actions
```

## License

[MIT License](LICENSE)

Copyright (c) 2026 Matthew Hetherington

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
