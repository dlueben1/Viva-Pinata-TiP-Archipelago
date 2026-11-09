This is a mod for TiP to add Archipelago Support!

# Client development

The client is a ReTiP Lua mod. Its source is in `client/`, with the mod manifest
at `client/mod.toml` and Lua scripts in `client/scripts/`. The scaffold currently
only logs that it loaded; it does not yet connect to Archipelago.

From the repository root, build the mod into `dist/TiPAP/`:

```powershell
.\scripts\build-client.ps1
```

To build and install it, close ReTiP and provide your existing mods directory:

```powershell
.\scripts\build-client.ps1 -ModsDirectory 'X:\Programs\TiP\mods'
```

The installer refuses to overwrite an existing `TiPAP` directory. Back up or
remove that directory before reinstalling. Configure ReTiP to load `TiPAP` and
enable script mods in `retip.toml`, preserving any other mods you use. After
launching ReTiP, look for `[TiPAP] Lua client loaded` in its log.

# FAQ

**Are other mods supported?**

This mod is in super early development and aims to change progression logic. Compatibility with other mods is not yet established.

# AI Disclosure

This project is AI-assisted, such that my Local LLM and occasionally Codex were used to help with the disassembly of VP TiP. It also has been used for simple boilerplate that would be annoying to scaffold out.

The architecture, features, and final code are all written and maintained by me. I'm not a Vibe Coder, I've been a Software Engineer for most of my life, so I only use AI to help speed-up the tedious work since this is a hobby project of mine.

With respect to @SolarCookies wishes, I have not used AI to run against any of their code specific to the recomp - I've only used it against the original Xbox 360 disassembly.

I'm also a human who makes mistakes, so if anything seems wrong with the repo, please reach out and I'd be happy to resolve any issues!
