# AI policy

## How Zinc is made

AI is a big part of how Zinc gets built. I design each feature, decide how it should work, and review, test, and refine every change before it ships. My commits that an agent meaningfully helped with have an `AI disclosure:` line in the commit message. I write every reply on issues, discussions, and pull requests myself.

## Contributing with AI

AI tools are welcome here, as long as a person stands behind and understands every contribution. Agents working in this repo have their own instructions in [AGENTS.md](AGENTS.md).

### Say when you used AI

If AI wrote or substantially shaped part of an issue, a pull request, or a comment, say so, name the tool, and say roughly how much it did. "Claude Code wrote the first draft, and I rewrote the error handling" is plenty. Simple autocomplete, a spellchecker, or asking a chatbot a question doesn't need a mention. The pull request template has a place for this.

### Understand what you send

You should be able to explain what your change does and how it fits into the rest of Zinc without asking an AI. If you can't, it isn't ready yet. This matters most around libghostty, ConPTY, and the renderer, where a change that looks right can still crash on someone else's machine.

### Test it yourself

Run the change on your own machine before you open a pull request, and say what you tried. Unverified AI output moves the work of checking it onto the person reviewing it.

### Send it yourself

Don't let an agent open issues or pull requests on its own. A person reads what's going out and sends it. If you send one anyway, say so, and expect it to be closed unless someone takes it over. For a new feature or a big refactor, the issue or discussion still comes first, as [CONTRIBUTING.md](CONTRIBUTING.md#before-you-start) says. This is especially important for AI-written work, because it takes the longest to review.

### Keep issues and comments human

AI can help you write an issue, but read it, cut it down, and make sure it describes what actually happened to you.

### No AI-generated art

Icons, images, videos, sounds, color schemes, and other media need to be made by a person. Code and text are fine.

## When a contribution ignores this policy

Every issue and pull request is read by a person, and that time is limited. Pull requests that ignore this policy are closed without review, and someone who keeps sending unchecked work may be blocked from the repo.

