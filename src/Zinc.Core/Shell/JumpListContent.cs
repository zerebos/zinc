namespace Zinc.Shell;

/// <summary>An icon for a jump list item: a file (an .ico, or an .exe/.dll and the icon's index in it); null = Zinc's.</summary>
public readonly record struct JumpIcon(string? Path, int Index = 0);

/// <summary>
/// One item: a launch of zinc.exe with <see cref="Arguments"/> (LaunchRequest's flags), shown as <see cref="Title"/>,
/// with <see cref="Description"/> as its tooltip.
/// </summary>
public sealed record JumpItem(string Title, string Arguments, string? Description, JumpIcon Icon);

/// <summary>A titled group of items above the tasks (the user can pin or remove these).</summary>
public sealed record JumpCategory(string Title, IReadOnlyList<JumpItem> Items);

/// <summary>What the taskbar button's jump list holds: categories, then the tasks at the bottom.</summary>
public sealed record JumpListContent(IReadOnlyList<JumpCategory> Categories, IReadOnlyList<JumpItem> Tasks);
