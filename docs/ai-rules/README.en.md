# Rules for AI assistants

[Русский](README.md) | **English**

The files in this folder are **not** development rules for the tool itself, but **artifacts for users**: you attach them to a project that holds an exported Confluence mirror so that an AI assistant works correctly with that tree (understanding the `index.html` format, the purpose of the `.id*` markers, the difference between pages and attachments, and the specifics of the Confluence Storage Format).

## Available rules

- [`local-mirror-format.mdc`](local-mirror-format.mdc) — a description of the structure and format of the local Confluence page mirror exported by the tool.

## How to attach

The file content is plain Markdown with YAML frontmatter; it is tool-independent. Choose how to attach it based on your AI assistant.

### Cursor

Copy the file into `.cursor/rules/` of your project with the exported pages:

```bash
cp docs/ai-rules/local-mirror-format.mdc /path/to/your-project/.cursor/rules/
```

The rule is picked up automatically (`alwaysApply: true` in the frontmatter).

### Claude Code

Option 1 — import it into your project's `CLAUDE.md`:

```markdown
@path/to/local-mirror-format.mdc
```

Option 2 — copy the file content (without the frontmatter) directly into `CLAUDE.md`.

### Other tools (Continue, Aider, Windsurf, ChatGPT, etc.)

Pass the file content into the system prompt / rules / context per your tool's conventions. The frontmatter block (between `---`) can be removed — it is used only by Cursor.
