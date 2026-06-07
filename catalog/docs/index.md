# Genetec Security Center Macros

I write macros for Genetec Security Center as part of my day job, and a handful of them turned out to be general enough to be worth sharing. They live here.

Every one of them started as a real problem on a real system. A door nobody remembered to add to the master access rule. A partition an integration could only half see. The sort of thing you tend to find at the worst possible moment. These macros close those gaps and keep them closed.

## Using one

A macro here is a single C# file. Open its page, read it, then paste the file into a Macro entity in the Genetec Config Tool. Security Center compiles it when you apply, so there is nothing to install.

Read the whole page before you run anything. The failure modes and risks sections are there because these macros act on live access control. Always try one in a lab system first.

## Make them your own

I wrote these for my environments, and yours will be different. Most pages have a customizing section near the bottom that points you at what to change, like how many partitions a mapping covers or which entity types get synced. Take a macro, adjust it, and make it fit your system.

Have a look at the [macros](macros/index.md), or read a bit [about me](about.md).
