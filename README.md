# Genetec Security Center Macros

A collection of C# macros for Genetec Security Center 5.13 (also targets 5.12). Each
macro is a single `.cs` file that you paste into a Macro entity in Config Tool. Security
Center compiles macros at runtime, so there is no local build and no unit tests.

## How this repo is organized

```
Macros/
  <Category>/                       grouped by what the macro is for
    <Macro Display Name>/
      <ClassName>.cs                the macro, the paste target for Config Tool
      README.md                     the operator and developer guide
      development/                  the macro's shipped spec and plan
TechSec Concepts/                   flat markdown for pre-macro ideas
docs/                               working specs, plans, and the conformance checklist
templates/                          the standards every macro follows
```

## The standard every macro follows

- **One folder per macro**, holding the `.cs` file, a `README.md` guide, and a
  `development/` folder.
- **A standard header** at the top of the `.cs` file. See `templates/macro-header.txt`.
- **A standard guide** with a fixed set of sections, including a flowchart and an
  architecture-impact diagram. See `templates/macro-guide-template.md`.
- **Human-written tone** in code and guides. No "e.g.", no em dashes, and nothing that reads as machine-generated. No company, customer, or site names.
- **A visibility decision.** Every macro is marked Public or Restricted. Restricted macros are kept internal and are never published to the public catalog. Publishing is fail-closed: only macros marked Public are published.

To bring an existing macro up to standard, follow `docs/conformance-checklist.md`.

## Where planning happens

Specs and plans are drafted under `docs/`. When a macro ships, its final spec and plan
are copied into that macro's `development/` folder as the permanent record. Raw ideas
that are not yet a macro live as flat markdown in `TechSec Concepts/`.

## Starting a new macro

The first three questions are always the macro name, the category it belongs in, and
whether it is event-driven or scheduled. The scaffold then creates the folder, the
`.cs` file with the standard header, and the guide from the template.
