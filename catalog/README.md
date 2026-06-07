# Catalog site

This folder holds the public catalog website for the macros in this repository. It is built with MkDocs and the Material theme. The site publishes only the macros that are marked Public, and it never includes Restricted macros or any planning notes.

## How publishing works

`build_catalog.py` reads every macro guide under `Macros/`. A macro is published only when its at-a-glance table has a Visibility row set to Public. The rule is fail-closed: Restricted, unmarked, or unreadable macros are skipped. The script copies each Public macro's `README.md` into `docs/macros/` and writes a section index. The `development/` folders are never read.

To publish a macro:

1. Confirm by security and IP review that the macro and its guide contain nothing sensitive and no company, customer, or site names.
2. Set the macro's Visibility to Public in both its header and its guide's at-a-glance table.
3. Rebuild. The page appears automatically.

## Build and preview locally

```bash
cd catalog
pip install -r requirements.txt
python build_catalog.py        # generates docs/macros from Public macros
mkdocs serve                   # preview at http://127.0.0.1:8000
```

`docs/macros/` and `site/` are generated and are not committed.

## Deploying to GitHub Pages

The workflow at `.github/workflows/deploy-catalog.yml` builds the site and deploys it to GitHub Pages on every push to `main`.

One thing to confirm before it works: publishing a public Pages site from a private repository requires a GitHub plan that allows it. If your plan does not, two options keep the source private:

1. Push only the built `site/` output to a separate public repository and serve Pages from there.
2. Use a host that publishes a public site directly from a private repository.

Until Pages is enabled and the plan is confirmed, you can still build and preview the site locally with the commands above.
