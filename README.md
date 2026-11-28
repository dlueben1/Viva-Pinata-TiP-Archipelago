This is a mod for TiP to add Archipelago Support! It's very early in development so the README is stil WIP.

It's been a journey - started as a Reloaded-II framework mod, then Lua, then back to C# and C++!

# Installation

(todo)

# Launching TiP on Windows

With Archipelago 0.6.7 and a modded ReTiP installation, open the Archipelago
Launcher, choose **Install APWorld**, and select `vp_tip.apworld`. Restart
the Archipelago Launcher after installing or updating the world.

1. Search for **Launch TiP** in the Archipelago Launcher and open it.
2. Enter the server hostname or URL, port (default `38281`), and optional password.
3. Browse to the player's `.aptip` patch from the host. Check the displayed seed
   and slot, then select `retip.exe` from the modded game installation.
4. Choose **Start**. The TiP window closes when ReTiP starts. Next time, the
   launcher remembers the executable. Use the same patch to resume that session.

**Save location.** Choose **Browse Files** in the Archipelago Launcher to open
its writable data directory. The `VivaAP` folder there contains `launcher.ini`
(game path only) and `sessions\<seed>_<slot>\`. Names that would conflict on
Windows use a folder prefixed with `~` and a short hash.

# FAQ

**Are other mods supported?**

This mod is in super early development and aims to change progression logic. Compatibility with other mods is not yet established.

# AI Disclosure

This project is AI-assisted, such that my Local LLM (Qwen) and occasionally Codex were used to help with the disassembly of VP TiP. It also has been used for boilerplate that would be annoying to scaffold out.

The architecture, features, and final code are all written and maintained by me. I'm not a "vibe coder", I've been a Software Engineer for most of my life, so I only use AI to help speed-up the tedious work since this is a hobby project of mine.

With respect to @SolarCookies wishes, I have not used AI to run against any of their code specific to the recomp - I've only used it against the original Xbox 360 disassembly.

I'm also a human who makes mistakes, so if anything seems wrong with the repo, please reach out and I'd be happy to resolve any issues!

# Credits

Huge shoutout to SolarCookies, who not only created ReTiP, but has been kind enough to help me out through the modding process!

Credit to StarlightNebula for the amazing logo!
