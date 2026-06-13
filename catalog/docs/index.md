# Genetec Security Center Macros

I have worked in electronic security my whole career, and over the years I kept building small Genetec Security Center macros to fix the same recurring problems. This site is where I share the ones that are general enough to help other teams.

None of these are products. They are ideas, written down as working code, that solved a real problem for me. If one of them helps your team, that is the point. And if you look at one and see a better way to do it, I want to hear about it so we can build something better together.

## What these are

A macro here is a single C# file that you paste into a Macro entity in the Genetec Config Tool. Security Center compiles it at runtime. There is no installer and no build step. Every macro page explains what the macro does, every piece of data it touches, how it can fail, and the risks to weigh before you run it.

## How to use one

1. Open the macro's page and read the guide end to end, especially the failure modes and the troubleshooting notes.
2. Copy the macro file into a new Macro entity in Config Tool.
3. Set the parameters the guide lists.
4. Test it in a lab or staging system before you point it at production. These macros act on live access control, so a careful first run matters.

## A note on safety

These are tools, not turnkey products. Read each guide, understand what the macro writes, and confirm the behavior in a non-production system first. Where a macro can change live configuration, its guide says so and explains how to roll back.

## Found a better way, or want to collaborate?

I would rather these get better over time than stay frozen. If you spot a bug, a sharper approach, or a problem worth solving, reach out on GitHub at [mnetardusaurora](https://github.com/mnetardusaurora) or email me at mnetardus@auroranexus.ai.

Browse the [macros](macros/index.md), or read more [about me](about.md).
