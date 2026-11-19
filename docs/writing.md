# Writing docs, comments, and commits

This guide is for the writing that lives around Zinc's code, which means the README, the docs, the changelog, code comments, and commit messages. Text inside the app (labels, descriptions, notices, the palette, `zn`'s output) follows [ux-writing.md](ux-writing.md) instead. The two guides share the rules in "The rules both guides share".

## The voice

Zinc's docs read like a developer who knows the code well explaining it to another developer, who is sitting next to them. They're plain and calm. They're specific about how things work, honest about what's fragile, and fair to the tools Zinc is built on. The personality is there, but it comes from word choice and rhythm, not from punctuation or jokes.

Most sentences are just clear. The voice shows in smaller things:

- Two short sentences where a generated text would write one long sentence with a colon in the middle.
- Contractions ("can't", "isn't", "it's", "you'd").
- Sentences that start with "But", "And", or "So" when that's how the thought goes.
- Small qualifiers that carry the logic, like "also", "still", "only", "instead", and "anyway". "Nerd Fonts also show prompt icons" means something different without the "also".
- Now and then, a soft word like "pretty", "a bit", or "something like", or an "Honestly," in front of something that's true.
- About once in a long section, a dry aside that comes from a real detail. "Nothing fails and nothing is logged. The pane keeps its last picture, even through a resize." The humor is in the fact, not bolted on.

## The rules both guides share

These come from [ux-writing.md](ux-writing.md#punctuation), which is the source for them. They hold everywhere, from a setting's description to a code comment.

- **No semicolons.** Two short sentences read better than one long one split in the middle.
- **A colon only before a list or code.** Inside the app, a colon can also separate a label from its value. Everywhere else, join the ideas with a word instead, like "because", "so", "which", "when", or "instead of".
- **No em dashes, no "...", and no exclamation marks.** An aside goes in parentheses or its own sentence.
- **A serial comma** before the last item of three or more ("tabs, splits, and folders").
- **Name the thing instead of writing "it".** "Shows and hides the quick terminal" says more than "Shows and hides it", and the reader never has to look back to find out what "it" was.
- **Full sentences, and shorthand spelled out.** A fragment gets a verb and a subject. "Slower." becomes "which is slower", "= none" becomes "for none", and "/" becomes "or".

A parenthesis is fine for a short example, like "(Cascadia Code, JetBrains Mono, Fira Code)". When it carries a rule, it becomes a sentence of its own. "If the scheme doesn't set one either, it's the background color" reads better than a rule tucked into brackets.

## How much voice each kind of text gets

The voice stays the same everywhere, but the room for it changes with the job.

- **docs/architecture.md and the other "how it works" docs** get the most. They walk through mechanisms, so they can ask the reader's question, show the obvious approach that fails, and allow the occasional aside. The canvas lifetime section is a good model.
- **docs/development.md** is drier, the way a reference page is drier than a tutorial. Steps and facts come first, with the reason in one clause.
- **The README** speaks to people who use Zinc, not people who work on it. It's specific and calm, and it follows "The README, the website, the changelog, and release notes" below.
- **The user guide on zincterminal.dev** is for people using Zinc, so it's closer to the README than to docs/architecture.md. It's task-first and in the second person: what to press or set, then what happens. It has little personality and no internals. Bold what someone clicks or switches, and quote a setting's name only when setting it to a value (set "Reopen windows" to "Start fresh"). Write shortcuts as whole chords, like Alt+Arrow and Ctrl+Wheel.
- **The changelog** is for users too. Each entry says what changed for them, in a sentence or two, with no story behind it.
- **Code comments** get almost none. They say what the code does or prevents, and stop.
- **Commit messages** get none in the subject, and a few plain sentences in the body when the reason isn't obvious.

## The README, the website, the changelog, and release notes

These keep the interface's tone, but their job is different. They explain what Zinc does and why someone might switch to it. The website means the landing page on zincterminal.dev, and its user guide has its own entry above.

- **Specific over superlative.** Name the detail. "Box drawing meets without seams in any font" beats "beautiful rendering". The interface names the familiar thing instead, and that difference is deliberate.
- **Calm.** No "blazing", "insanely", or "game-changing".
- **Headlines can be phrases.** Taglines, page titles, and the lead under a page's main heading can be phrases instead of sentences, like "Every cell, exactly right." The copy under them follows the usual rules.
- **Honest about limits.** Say plainly what isn't there yet. The interface leaves limits out, but the docs and release notes don't.
- **Credit Ghostty accurately.** Say "built on libghostty", "uses Ghostty's terminal engine", or "the same core that powers Ghostty". Never say "Ghostty for Windows", "a Ghostty port", or anything that suggests an official Ghostty project.
- **The name** follows ux-writing.md's [The product's name](ux-writing.md#the-products-name), so it's "Zinc" in prose, never "Zinc Terminal".

Older changelog entries can be reworded for clarity, as long as their headings stay exactly as they are, because the release tooling reads them.

## How the docs explain things

### Answer the reader's question

A heading can be the question a reader would ask, like "Why would a pane stop drawing?", or it can name the thing, like "Canvas lifetime". Pick whichever is clearer for that section, so the headings don't all follow one pattern. Headings are never clever. Then answer the question plainly, in the first sentence or two.

### Correct the picture the reader brings

When there's an obvious way to think about something, or an obvious way to do it, and it's wrong, say so in the present tense and then say what's really true. "You'd expect a replaced canvas to be collected like anything else, but Win2D may still have it registered with the window" teaches more than the rule alone, because it fixes the picture the reader arrived with. The story of how that was found (what broke, what was tried, and when) stays out of the docs. Only the lesson stays.

### Walk through it in order

A mechanism reads best as the order of events, with each sentence leaning on the one before. "A key's `WM_CHAR` arrives after its `KeyDown` and before its `KeyUp`. The terminal skips the character only when `KeyDown` already dealt with that key." "We" fits here, when the doc walks through an example with the reader.

### Read code back

After a code block, a diagram, or a sequence of steps, say in a sentence or two what it does. A reader who skims the code should still get the point.

### Say whose limit it is

When something can't be done, say where the limit comes from. "This is WinUI's behavior, not something Zinc can change" is clearer than a vague "this can happen". Say it once and calmly, and be fair about it. A framework that makes something hard usually made something else easy.

### End on the consequence

A section ends when the explanation is done, often on what happens if you break the rule. It doesn't end on a sentence that sums up what was just said.

### "You", "we", and "Zinc"

"You" is the reader, and "Zinc" is what the app does. "We" is for walking through something together. "I" doesn't appear in `docs/`, the README, or the changelog. CONTRIBUTING.md and AI_POLICY.md are the maintainer speaking, so they use "I".

### Shape follows the content

A chain of cause and effect is prose. A set of separate facts that a reader scans for the one they need is a list, and the list opens with a sentence that says what it holds. A simple rule gets one sentence, and a mechanism gets a short walk-through. Don't open a doc or a section with a colon, and don't add sentences that only announce what the section is about to do. "This guide is about how Zinc works inside. It covers where things live, how the pieces talk to each other, and the rules the code relies on." is a better opening than "This is the guide to Zinc's internals: layout, threads, and drawing."

## Before and after

These pairs come from rewrites in Zinc's own history, where the first version was generated and the second is the one that shipped.

| Before | After |
|---|---|
| by default: a cached release list keeps | by default, because a cached release list keeps |
| Off: it all goes | When off, all of it goes |
| Shows and hides it. | Shows and hides the quick terminal. |
| The focused pane's scrollbar, expanded and kept so. | Expands the focused pane's scrollbar and keeps it expanded. |
| No window: the hotkey works. | No window opens. The hotkey works. |
| 1: off; 4.5: the usual level | 1 turns it off, 4.5 is the usual accessibility level, and 21 makes all text black or white. |
| (without either, the background color) | If the scheme doesn't set one either, it's the background color. |
| Velopack couldn't show progress: a tester saw 0% for a whole download. | Velopack then can't show download progress. |
| while Windows' Animation effects are off | while "Animation effects" is off in Windows Settings |

Each "after" does one of a few things. It turns a colon into a sentence or a joining word, names the thing, gives a fragment its verb, turns a rule out of brackets into a sentence, drops the story but keeps the effect, or uses the name the reader sees on screen.

## Code comments

A comment says what the code does or what it prevents, in the present tense. When it describes a member or a block, it starts with the verb ("Opens the context menu as a right-click on a cell would."). The story behind the code stays out of the comment, and only a short clause about the risk remains. "Velopack then can't show download progress" belongs in the comment, but the tester who first saw 0% for a whole download doesn't.

Leave out comments that only restate the code. A short fragment is often best when it says one clear thing, and a reason that takes a few sentences gets a few sentences. Say who does what in plain verbs, like "the caller sends the message after the transaction commits", not figurative ones. Code names go in backticks. [development.md](development.md#code-style) has the rules for line width and doc summaries.

## Commit messages

The subject is short, lowercase, active, and imperative, with no trailing period, like `fix powerline triangles` or `show real status on the error page`. Keep it short without clipping it, so an article or a short list is fine where it reads better. Add a body only when the reason isn't obvious, in a few plain sentences. This is the style the history uses, and [CONTRIBUTING.md](../CONTRIBUTING.md) says contributors don't have to match it.

## Signs of generated text

Text reads as generated when it has a template's shape rather than its content's. Watch for these.

- Every section or bullet built the same way, such as a bold claim followed by an explanation, all the way down a list.
- Bold lead-ins on every bullet, lists of exactly three, and a summary sentence at the end of each section (one that starts "This ensures").
- Openers that only announce what follows, like "This section covers the following".
- Filler and hedges like "it's worth noting", "note that", "essentially", "in order to", "robust", and "seamless".
- Even polish everywhere. Real docs are short where the thing is simple and longer where it isn't.
- Clever headings and lead-ins, like "Default colors aren't colors.", where naming the thing would be clearer.
- Personality added on top, like a joke that isn't about anything real, or an "Honestly," in front of something that isn't a confession.
