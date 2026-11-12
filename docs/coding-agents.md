# Coding agent setup

This repository uses a hybrid setup: essential, self-contained skills are checked in; larger upstream bundles are plugins with overridable repository defaults. Building, running, and testing the application does not require a plugin. Follow the [getting started guide](getting-started.md) and repository instructions when no plugin is installed.

## Repository-local guidance

| Skill | Purpose |
| --- | --- |
| `aspire` and its companion workflow skills | Official Aspire guidance for AppHost wiring, lifecycle, diagnostics, and approval-first deployment or migration workflows. |
| `tunit` | Official TUnit guidance for tests, assertions, fixtures, and framework-specific execution. |
| `optimizing-ef-core-queries` | EF Core query performance guidance. |

The same skill content is stored under `.agents/skills/` and `.claude/skills/`. Claude Code discovers the latter; compatible clients such as OpenCode can discover either. Keep matching copies synchronized, including source-tracking metadata. Do not create another copy for OpenCode.

The standalone `run-tests` skill is intentionally not vendored: it refers to `filter-syntax`, `platform-detection`, and other testing skills. Use the complete optional testing plugin rather than copying individual interdependent skills or assuming they are available to every contributor.

### Aspire agent setup

The Aspire bundle was generated with Aspire CLI 13.6.4 using `aspire agent init`, not hand-written.
The `aspire` router invokes six companion skills, so keep the complete bundle and its reference
files together. Refresh both repository-local copies from the repository root:

```powershell
aspire agent init --non-interactive --workspace-root . `
    --skill-locations standard,claudecode `
    --skills aspire,aspire-init,aspireify,aspire-orchestration,aspire-monitoring,aspire-deployment,aspire-project-v2-migration `
    --mcp=false
```

The explicit skill list excludes optional companion tools such as `playwright-cli` and
`dotnet-inspect`. `--mcp=false` leaves existing MCP configuration unchanged; these skills use
the Aspire CLI without requiring an MCP server. The installer also refreshes user-level
`~/.agents/skills` and installs agent usage telemetry hooks by default. Set
`ASPIRE_CLI_TELEMETRY_OPTOUT=true` to opt out of telemetry.

Installing guidance does not execute or approve its workflows. This repository's AppHost is
for **local development only**. Production continues through the existing Docker/CI pipeline,
not `aspire deploy`; do not introduce publish-mode provisioning or migrate project resources
merely because those skills are installed. See the [standalone Aspire instructions](../README.md#standalone-aspire-development)
for the AppHost path and runtime prerequisites.

## Optional upstream plugins

| Plugin | Recommendation |
| --- | --- |
| `dotnet-test` | Enabled by repository default for broader test execution, generation, coverage, and test quality work. Complements the official TUnit skill. |
| `dotnet` | Optional for C# refactoring and language-server integration where supported. |
| `dotnet-msbuild` | Optional for recurring build diagnostics and generated-file investigations. |

Install only the bundles you need. A plugin preserves its sibling skills and supporting files, but does not necessarily include every cross-plugin reference. For example, `including-generated-files` belongs to `dotnet-msbuild`, not `dotnet`. If a referenced capability is unavailable, report the limitation and use the repository's existing tools; do not install more plugins automatically.

The upstream packaging reviewed for this guide is [dotnet/skills at commit `96c3d68dad2855e3ab5178aa1a262789d86a1466`](https://github.com/dotnet/skills/tree/96c3d68dad2855e3ab5178aa1a262789d86a1466). Its `dotnet-test` manifest reports version `1.0.1`. This is a reviewed source snapshot, not a claim that installation or model behavior has been tested in every client. Repository marketplace declarations and the commands below follow the marketplace's current revision; they do not pin that snapshot or opt into background auto-updates. Review the installed version and source before enabling or updating a plugin.

## Repository defaults and opting out

`.github/copilot/settings.json` and `.claude/settings.json` register `dotnet/skills` as `dotnet-agent-skills` and declare `dotnet-test@dotnet-agent-skills` enabled. These are client-specific defaults, not an enforced policy or a universal installation mechanism. Managed organization policy takes precedence.

Copilot CLI documents declarative auto-install through repository settings; Copilot in the cloud also reads the plugin keys. Other Copilot surfaces may require marketplace registration or installation through their UI. Claude Code reads project settings only after workspace trust, and a project declaration alone does not guarantee installation; see the installation instructions below.

To opt out, add this object to your personal repository-local override file, merging it with existing settings:

```json
{
  "enabledPlugins": {
    "dotnet-test@dotnet-agent-skills": false
  }
}
```

Use `.github/copilot/settings.local.json` for Copilot CLI or `.claude/settings.local.json` for Claude Code. Both are gitignored. Other clients should use their supported local plugin controls. Do not change the committed defaults merely to opt out personally.

## Claude Code and Copilot CLI

The repository declares the marketplace and testing plugin. If they are not installed, use the upstream marketplace in each client's interactive session:

```text
/plugin marketplace add dotnet/skills
/plugin install dotnet-test@dotnet-agent-skills
```

Restart the session after installing, then inspect the available skills and agents using the client's discovery UI. Enable `dotnet` or `dotnet-msbuild` from the same marketplace only when needed.

These are client operations, not shell commands or setup steps that agents should execute on their own. For Claude Code, after accepting workspace trust, install from a shell if the project-enabled plugin is missing:

```text
claude plugin install dotnet-test@dotnet-agent-skills --scope project
```

Run `/reload-plugins` or start a new session afterward. Claude and Copilot use their own manifests and component loaders; matching installation commands do not guarantee identical agent behavior.

## VS Code with GitHub Copilot

In your personal VS Code settings, add the marketplace without replacing any existing entries:

```jsonc
{
  "chat.plugins.enabled": true,
  "chat.plugins.marketplaces": ["dotnet/skills"]
}
```

Use `/plugins` in Copilot Chat or the `@agentPlugins` Extensions filter to browse and install `dotnet-test` if the repository default has not made it available. Plugin availability depends on your VS Code version and organization policy. This repository does not change VS Code workspace settings or enable every plugin from the marketplace.

## GitHub Copilot app

If the repository default has not made the plugin available, open **Customize**, use the marketplace settings control to add `dotnet/skills`, then find and install `dotnet-test`. Manage enablement in **Customize > Installed**.

The plugin defaults belong in the documented `.github/copilot/settings.json`, not an undocumented plugin key or installation script in `.github/github-app.yml`. The latter configures app instructions, scripts, and automation behavior; it is not a plugin manifest.

## OpenCode and other clients

OpenCode documents project skill discovery in both `.agents/skills/` and `.claude/skills/`, so the checked-in baseline does not need a shim. Do not assume OpenCode's JavaScript plugin mechanism can install the upstream Claude/Copilot plugin package.

For optional testing guidance in a client without a compatible plugin loader, use a separate local checkout of the reviewed upstream revision and the client's documented additional skill-directory mechanism, if available. Expose the whole `plugins/dotnet-test/skills` directory, including supporting files, rather than copying selected `SKILL.md` files. This provides skills only; it does not install the upstream agents, hooks, MCP servers, or LSP integration. Do not rely on workflows that require those unavailable components.

## Verification and maintenance

After installation or an update:

1. Confirm the checked-in Aspire bundle, TUnit, and EF Core skills are discoverable.
2. Confirm the testing bundle exposes `run-tests`, `filter-syntax`, and `platform-detection`.
3. Inspect available agents separately; skill discovery does not prove custom-agent compatibility.
4. Exercise a representative task in your client and report missing references or components.

Avoid duplicate same-named local and plugin skills. The repository does not vendor `run-tests`; if you enable `dotnet-data` separately, avoid exposing its EF Core skill alongside the checked-in copy where the client allows per-skill controls.

Update both checked-in skill copies together and inspect the diff. Review optional plugin updates in each client before enabling them; update this guide's reviewed snapshot when evaluating a new upstream revision. Do not describe a version as tested until its discovery and representative behavior have actually been verified in the named client.

## References

- [Upstream plugin installation and compatibility](https://github.com/dotnet/skills#installation)
- [Claude Code skill discovery](https://code.claude.com/docs/en/skills)
- [Claude Code marketplaces](https://code.claude.com/docs/en/plugin-marketplaces)
- [VS Code plugin authoring and client formats](https://code.visualstudio.com/docs/agents/reference/agent-plugin-reference)
- [GitHub Copilot app customization](https://docs.github.com/en/copilot/how-tos/github-copilot-app/customize-github-copilot-app)
- [Copilot repository plugin defaults](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-config-dir-reference#repository-settings-githubcopilotsettingsjson)
- [Claude project plugin installation behavior](https://code.claude.com/docs/en/plugins/loading#enabled-in-project-settings-but-not-installed)
- [OpenCode skill discovery and additional sources](https://opencode.ai/docs/skills/)
- [Agent Plugins specification](https://agent-plugins.org/specification)
