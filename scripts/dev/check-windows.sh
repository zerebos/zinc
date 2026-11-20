#!/bin/sh
# Compile-checks the Windows build on macOS or Linux, where it can't be built for real. It compiles Zinc.Core and the
# tests for Windows, then the app's C#. It runs nothing, so it finds compile errors only.
#
# The app's own build needs Windows three times over: the XAML compiler, the manifest tool, and MakePri. So this
# stops after the C# compile, and stands in for the first two. The XAML compiler's output is replaced by a generated
# file with an empty InitializeComponent and a field for each x:Name in MainWindow.xaml. Event handlers that only
# XAML refers to then look unused, so IDE0060 warnings from the app are expected here and nowhere else.
#
# Usage: scripts/dev/check-windows.sh [extra dotnet build arguments, such as --no-incremental]
set -eu
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$root/src/Zinc/obj/check-windows
mkdir -p "$work"

python3 - "$root/src/Zinc/MainWindow.xaml" "$work" <<'PY'
import re, sys
xaml, work = open(sys.argv[1]).read(), sys.argv[2]
fields = [(m.group(1).split(':')[-1], m.group(2)) for m in re.finditer(r'<([A-Za-z:]+)\b[^<>]*?x:Name="(\w+)"', xaml, re.S)]
lines = ['using Microsoft.UI.Xaml.Controls;', 'using Microsoft.UI.Xaml.Shapes;', 'namespace Zinc;',
         'public partial class App { public void InitializeComponent() { } }',
         'public sealed partial class MainWindow', '{', '    public void InitializeComponent() { }']
lines += [f'    private {kind} {name} = null!;' for kind, name in fields] + ['}']
open(f'{work}/XamlStandIns.cs', 'w').write('\n'.join(lines) + '\n')
open(f'{work}/check.targets', 'w').write(f'''<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Zinc'">
    <Page Remove="@(Page)" />
    <ApplicationDefinition Remove="@(ApplicationDefinition)" />
    <Compile Include="{work}/XamlStandIns.cs" />
  </ItemGroup>
</Project>
''')
PY

# mt.exe merges manifests. An empty file where its output goes is enough to get to the compile.
cat > "$work/mt.sh" <<'MT'
#!/bin/sh
for a in "$@"; do case $a in -out:*) out=${a#-out:}; mkdir -p "$(dirname "$out")"; : > "$out";; esac; done
MT
chmod +x "$work/mt.sh"

failed=0
# check <name> <the assembly the compile writes> <dotnet build arguments>
# Only compiler output counts, because the tests' and the app's builds go on to steps that need Windows (copying the
# native DLLs, MakePri) and fail there. A missing assembly means the compile never ran.
check() {
    name=$1 assembly=$2; shift 2
    rm -f "$assembly"
    dotnet build "$@" -p:ZincWindows=true -p:EnableWindowsTargeting=true -nologo > "$work/$name.log" 2>&1 || true
    found=$(grep -E ' (error|warning) (CS|IDE|CA|NETSDK|NU)[0-9]+' "$work/$name.log" | grep -v "$expected" | sed 's/ \[.*//' | sort -u || true)
    if [ -n "$found" ] || [ ! -s "$assembly" ]; then
        failed=1
        echo "$name: FAILED (full log: $work/$name.log)"
        [ -z "$found" ] || printf '%s\n' "$found" | head -40
    else
        echo "$name: compiles for Windows"
    fi
}

obj=obj/x64/Debug/net10.0-windows10.0.26100.0
expected='^$'
check core "$root/src/Zinc.Core/$obj/Zinc.Core.dll" "$root/src/Zinc.Core/Zinc.Core.csproj" -t:Compile "$@"
check tests "$root/tests/Zinc.Tests/$obj/win-x64/Zinc.Tests.dll" "$root/tests/Zinc.Tests/Zinc.Tests.csproj" -t:Compile "$@"
expected='IDE0060'
check app "$root/src/Zinc/$obj/win-x64/zinc.dll" "$root/src/Zinc/Zinc.csproj" -t:Compile "-p:ManifestTool=$work/mt.sh" "-p:CustomAfterMicrosoftCommonTargets=$work/check.targets" "$@"
exit $failed
