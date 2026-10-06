# Zinc's shell integration for elvish.
#
# Zinc starts elvish with -rc pointing here, so this file runs instead of your rc.elv: it runs yours first, as
# elvish would have (its variables and functions are added to the prompt's namespace), then adds invisible marks
# around your prompt and commands (OSC 133: prompt start, command running with its text, finished with its exit
# code) and reports the current folder. Zinc uses them to jump between prompts, select a command's output, mark
# failed commands and open new tabs in the same folder.
#
# elvish draws its prompt in the background, so there's no mark where typing starts. To turn this off:
# shell-integration = "off" under [terminal] in config.toml (or in one [profile.x]).

use os
use str
use runtime
use platform

# Your rc.elv (or the -rc you gave, which Zinc passes in ZINC_ELVISH_RC).
var zinc-si-rc = $runtime:rc-path
if (has-env ZINC_ELVISH_RC) {
  set zinc-si-rc = $E:ZINC_ELVISH_RC
  unset-env ZINC_ELVISH_RC
}
if (and (not-eq $zinc-si-rc $nil) (os:is-regular $zinc-si-rc)) {
  try {
    eval (slurp < $zinc-si-rc) &on-end={|ns|
      var vars = [&]
      for name [(keys $ns)] {
        if (not (str:has-prefix $name zinc-si-)) {   # (the eval starts from this file's names)
          set vars[$name] = $ns[$name]
        }
      }
      edit:add-vars $vars
    }
  } catch e {
    echo 'Error in '$zinc-si-rc':' >&2
    show $e >&2
  }
}

# Percent-encodes text byte by byte (for cmdline_url and folder URIs).
fn zinc-si-encode {|text|
  var out = ''
  for byte [(str:to-utf8-bytes $text)] {
    var n = (num $byte)
    var c = (str:from-codepoints $n)
    if (or (and (>= $n 0x30) (<= $n 0x39)) (and (>= $n 0x41) (<= $n 0x5A)) (and (>= $n 0x61) (<= $n 0x7A)) (has-value [/ . _ - '~'] $c)) {
      set out = $out$c
    } else {
      set out = $out'%'(printf '%02X' $n)
    }
  }
  put $out
}

# The last command's exit code: 0, an external program's status, else 1 (an elvish error).
fn zinc-si-status {|m|
  if (eq $m[error] $nil) {
    put 0
  } else {
    try {
      put $m[error][reason][exit-status]
    } catch {
      put 1
    }
  }
}

# A prompt starts (A), with the folder (Windows: OSC 9;9 with the path; elsewhere OSC 7, a file:// URI).
set edit:before-readline = (conj $edit:before-readline {
  printf "\e]133;A;aid=%s\a" $pid
  if $platform:is-windows {
    printf "\e]9;9;%s\a" $pwd
  } else {
    printf "\e]7;file://%s%s\a" (platform:hostname) (zinc-si-encode $pwd)
  }
})

# The command runs (C, with its text), and how it ended (D).
set edit:after-readline = (conj $edit:after-readline {|line|
  printf "\e]133;C;cmdline_url=%s\a" (zinc-si-encode $line)
})
set edit:after-command = (conj $edit:after-command {|m|
  printf "\e]133;D;%s\a" (zinc-si-status $m)
})
