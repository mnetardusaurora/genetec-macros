# Genetec Security Center Macros

I build and run Genetec Security Center macros in the field, and this is where I share the ones that are general enough to help other security teams. Each macro solves a specific, recurring problem in access control and physical security, and each comes with a plain-language guide.

Every one of them started as a real problem on a real system. A door nobody remembered to add to the master access rule. A partition an integration could only half see. The sort of thing you tend to find at the worst possible moment. These macros close those gaps and keep them closed.

## Using one

A macro here is a single C# file. Open its page, read it, then paste the file into a Macro entity in the Genetec Config Tool. Security Center compiles it when you apply, so there is nothing to install.

1. Open the macro's page and read the guide end to end, especially the failure modes and risks.
2. Copy the macro file into a new Macro entity in Config Tool.
3. Set the parameters the guide lists.
4. Test it in a lab or staging system before you point it at production. These macros act on live access control, so a careful first run matters.

## Make them your own

These are tools, not turnkey products. Read each guide, understand what the macro writes, and confirm the behavior in a non-production system first. Where a macro can change live configuration, its guide says so and explains how to roll back.

Have a look at the [macros](macros/index.md), or read a bit [about me](about.md).
