"""Puts each version's section of RELEASE-NOTES.md at the top of its GitHub Release page.

Keeps the page's install text, replaces any earlier "What's new" part, and joins the file's wrapped
lines (GitHub shows every line break on a Release page). Runs from update-release-notes.yml.
"""
import json, os, re, subprocess

REPO = os.environ["GITHUB_REPOSITORY"]
NOTES = open("RELEASE-NOTES.md", encoding="utf-8").read()
LINK = f"All changes: [RELEASE-NOTES.md](https://github.com/{REPO}/blob/main/RELEASE-NOTES.md)"


def section(version):
    m = re.search(r"(?ms)^## " + re.escape(version) + r"(?=[ \r\n])[^\n]*\n(.*?)(?=^## |\Z)", NOTES)
    if not m:
        return None
    return re.sub(r"(?<=\S)[ \t]*\r?\n(?![ \t]*\r?\n)(?![ \t]*- )(?!#)[ \t]*", " ", m.group(1).strip())


def install_text(body):
    body = (body or "").replace("\r\n", "\n")
    if "## How to install" in body:
        body = body.split("## How to install", 1)[1]
    body = re.sub(r"\n*All changes: \[RELEASE-NOTES\.md\]\([^)]*\)\s*$", "", body)
    return body.strip()


releases = json.loads(subprocess.check_output(["gh", "api", f"repos/{REPO}/releases?per_page=100"]))
for release in releases:
    tag, notes = release["tag_name"], section(release["tag_name"])
    if notes is None:
        print(f"{tag}: no section in RELEASE-NOTES.md, left as it is")
        continue
    body = f"## What's new in {tag}\n\n{notes}\n\n## How to install\n\n{install_text(release['body'])}\n\n{LINK}\n"
    if body == (release["body"] or "").replace("\r\n", "\n"):
        print(f"{tag}: already up to date")
        continue
    subprocess.run(["gh", "api", "-X", "PATCH", f"repos/{REPO}/releases/{release['id']}", "-f", f"body={body}",
                    "--silent"], check=True)
    print(f"{tag}: updated")
