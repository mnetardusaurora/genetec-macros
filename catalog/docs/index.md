# Genetec Security Center Macros

I build and run Genetec Security Center macros in the field, and this is where I share the ones that are general enough to help other security teams. Each macro solves a specific, recurring problem in access control and physical security, and each comes with a plain-language guide.

## What these are

A macro here is a single C# file that you paste into a Macro entity in the Genetec Config Tool. Security Center compiles it at runtime. There is no installer and no build step. Every macro page explains what the macro does, every piece of data it touches, how it can fail, and the risks to weigh before you run it.

## How to use one

1. Open the macro's page and read the guide end to end, especially the failure modes and risks.
2. Copy the macro file into a new Macro entity in Config Tool.
3. Set the parameters the guide lists.
4. Test it in a lab or staging system before you point it at production. These macros act on live access control, so a careful first run matters.

## A note on safety

These are tools, not turnkey products. Read each guide, understand what the macro writes, and confirm the behavior in a non-production system first. Where a macro can change live configuration, its guide says so and explains how to roll back.

Browse the [macros](macros/index.md), or read more [about me](about.md).
