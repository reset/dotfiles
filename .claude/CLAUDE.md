# Jamie Stormbreaker — Global Claude Config

## Who I Am
- **Role:** Founder / COO, One More Game (OMG) — remote game studio based in Seattle
- **Email:** jamie@onemoregame.com
- **Handle:** @resetexe
- **Primary language:** C# (principal engineer level)
- **Secondary languages:** Ruby, Python, Rust, Erlang, Terraform (HCL), Nix
- **Game director / lead designer** on SWAPMEAT and Spellcraft

## One More Game Context
- Small remote studio
- Jira project keys: SWAPMEAT (`SMD`), Spellcraft (`rog`)
- Uses Ramp for spend, Gusto for payroll, WTIA for group health benefits
- Fractional CFO: Jim Garbarini (Financial Works Inc.)
- Benefits broker: Jodi Hensley (Pro Benefits WA)
- Business partner and co-founder: Patrick Wyatt

## Code Style & Engineering
- C# is primary — always prefer idiomatic C# over generic solutions
- Unity game development context unless specified otherwise
- Prefer targeted, minimal changes over broad rewrites
- Ask before destructive git operations
- Do not commit unless explicitly requested
- **Never** add `Co-Authored-By` trailers (or any AI attribution) to commit messages
- **Never** modify git config (user.name, user.email, commit.gpgsign, etc.) or pass flags that bypass GPG signing (`--no-gpg-sign`, `-c commit.gpgsign=false`)
- Principal engineer level — skip basics, go deep

### C# Patterns (from codebases)
- Private fields prefixed `m_`, statics prefixed `s_` — no exceptions
- Constructor injection everywhere; VContainer in Unity, Microsoft.Extensions.DependencyInjection in tools
- `sealed` on internal/non-inherited classes by default
- Explicit interface implementations when the interface method isn't part of the public API (e.g. `Task IHostedService.StartAsync(...)`)
- Discard fluent returns: `_ = services.AddSingleton<Foo>()`
- Comment-block section headers to organize class members:
  ```
  //
  // Public API
  //
  ```
- Enum types prefixed with `E` (e.g. `EBuildPlatform`), interfaces with `I`
- MonoBehaviour derivatives suffixed with `Behavior`
- Async methods suffixed with `Async`
- OTBS (opening brace on same line), 4-space indent, no `#region`

### CLI Design
- CLI tools should be case-insensitive for commands, flags, and arguments wherever possible

### Infrastructure & Tooling
- Nix (`shell.nix`) for reproducible dev environments
- Makefiles as the universal entry point for all workflows (build, test, publish, terraform)
- Buildkite for CI/CD pipelines
- Terraform with Azure backend for infrastructure; modular layout (environments/, modules/)
- AWS for serverless (Lambda via `dotnet lambda`)
- Steam for game distribution (steamcmd integration)
- `omgcmd` — internal scaffolding tool that generates Makefiles, Nix configs, Terraform boilerplate
- Blender pipeline (BEngine) with Python scripting for procedural content

## Email Drafting
If an email is being drafted and it doesn't have a signature, include this one:
```
Jamie Stormbreaker
Founder / COO - One More Game
@resetexe
```

Style rules:
- 3–8 sentences unless it's a formal investor update
- Open with "Hey [name]," for everyone (internal and external)
- Be direct and matter-of-fact; never accusatory
- State what I want going forward, not what went wrong
- Never use: "undermining," "bypassing," "failure to," "unacceptable"
- Use dashes (—) for asides, not semicolons
- Frame direction to teammates as collaborative, not commanding
- Close casually — "Cheers!" or "Let me know if you need anything"
- Use exclamation marks for warmth, not urgency

## Claude Config Layering

Configuration is layered — each layer has a job:

| Layer | Where | What goes here |
|-------|-------|----------------|
| **Global** (this file) | `~/.claude/CLAUDE.md` | My identity, communication style, personal preferences. Duplication with project layers is fine — this is my private fallback and won't exist for other developers. |
| **Project** | `<repo>/CLAUDE.md` | Architecture, conventions, shared agent defaults (personality baseline, work habits, code rules). Anything common to all agents lives here once. |
| **Agent** | `<repo>/.claude/commands/<name>.md` | Only what's unique to that persona — domain scope, domain-specific personality, domain-specific rules. Must be portable. |

**When editing shared files:**
- Command files in `.claude/commands/` are shared with the entire team via git. They must be **portable** — never reference me by name, use my personal details, or embed information from this global config.
- Use role-based language ("the person you're working with," "the game director") instead of names.
- Before adding something to an agent file, check whether project `CLAUDE.md` or `CODE_STYLE.md` already covers it. If it does, don't repeat it.
- When a rule applies to multiple agents, put it in the "Agent Persona Defaults" section of project `CLAUDE.md`, not in each agent file.
- Always review command files for personal information leakage before finishing.

## Naming Preferences
- Prefer boring, descriptive names over clever or cute names for tools, projects, and systems
- Names should be easy to explain to someone who hasn't seen them before
- Clarity over creativity — if someone can guess what it does from the name, it's a good name

## Communication Style (General)
- Direct and concise — I don't need hand-holding or excessive caveats
- Don't restate my question back to me
- Skip preamble; lead with the answer
- Use prose over bullet points for explanations
- I appreciate being corrected if I'm wrong
- Ask clarifying questions before giving answers if you're unsure. 
- When brainstorming, I like it when you play devil's advocate. If you're unsure about something, give me a "percentage" of how sure you are. I don't expect it to be exactly accurate, but to help me decide what to do with the information.  Cite reasons you're sure or unsure.
- Do not default to sycophantic responses. I use Claude a lot of brainstorming, and I enjoy genuine debate where warranted. In personal situations or other human interactions, tell me what I need to hear, not what you think I want to hear. This is very valuable to me and will not result in me liking you less.

## Retro Gaming Context (personal projects / side convos)
- Own a RetroTink 4K (RT4K) upscaler; primary signal path is RGB SCART
- Collector across: Sega Genesis/CD/32X/Saturn/Dreamcast, PS1/PS2/PSP/PS Vita
- Dreamcast: using VGA output into RT4K; actively using Wobbling Pixels profile pack
- Comfort level: hardware-level nuance including VA revisions, sync types, profile config

