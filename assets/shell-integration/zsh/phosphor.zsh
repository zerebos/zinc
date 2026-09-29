# Phosphor's shell integration for zsh, part 2 (loaded by .zshenv, next to this file).
#
# It changes nothing you see: it adds invisible marks around your prompt and commands (OSC 133:
# prompt start, where typing starts, command running with its text, finished with its exit code)
# and reports the current folder (OSC 7). Phosphor uses them to jump between prompts, select a
# command's output, mark failed commands and open new tabs in the same folder.
#
# It sets itself up at the first prompt, after your startup files. Marks someone else already
# sends are left to them: powerlevel10k's own integration (POWERLEVEL9K_TERM_SHELL_INTEGRATION=true)
# or hand-written hooks; then only what they lack is added (p10k reports no folder). To turn this
# off: shell-integration = "off" under [terminal] in config.toml (or in one [profile.x]).

[[ -z "${__phosphor_si_loaded-}" ]] || 'builtin' 'return' 0
'builtin' 'typeset' -g __phosphor_si_loaded=1
'builtin' 'autoload' -Uz add-zsh-hook

__phosphor_si_boot() {
    add-zsh-hook -d precmd __phosphor_si_boot
    'builtin' 'local' f marks=1 folder=1
    [[ "${POWERLEVEL9K_TERM_SHELL_INTEGRATION-}" != true ]] || marks=
    [[ "$PS1" != *'133;'* ]] || marks=
    for f in $precmd_functions $preexec_functions $chpwd_functions; do
        [[ "$f" != __phosphor_si_* ]] || continue
        [[ "${functions[$f]-}" != *'133;'* ]] || marks=
        [[ "${functions[$f]-}" != *']7;'* ]] || folder=
    done

    if [[ -n "$folder" ]]; then
        add-zsh-hook chpwd __phosphor_si_report_folder
        __phosphor_si_report_folder
    fi

    if [[ -n "$marks" ]]; then
        # The exit code is read first thing, the marks go last (after a prompt theme builds PS1).
        precmd_functions=(__phosphor_si_status ${precmd_functions[@]} __phosphor_si_precmd)
        add-zsh-hook preexec __phosphor_si_preexec
        __phosphor_si_precmd
    fi
}

# Percent-encoded, byte by byte (for OSC 7 and C's cmdline_url).
__phosphor_si_encode() {
    'builtin' 'emulate' -L zsh -o extended_glob
    'builtin' 'local' LC_ALL=C
    REPLY=${1//(#m)[^a-zA-Z0-9\/._~-]/%${(l:2::0:)$(([##16]#MATCH))}}
}

__phosphor_si_report_folder() {
    'builtin' 'local' REPLY
    __phosphor_si_encode "$PWD"
    'builtin' 'print' -nr -- $'\e]7;file://'"${HOST-}$REPLY"$'\e\\'
}

__phosphor_si_status() { __phosphor_si_last=$? }

typeset -g __phosphor_si_b=$'%{\e]133;B\a%}'

__phosphor_si_precmd() {
    [[ -z "${__phosphor_si_prompted-}" ]] || 'builtin' 'print' -nr -- $'\e]133;D;'"${__phosphor_si_last-0}"$'\a'
    __phosphor_si_prompted=1
    'builtin' 'print' -nr -- $'\e]133;A\a'
    [[ "$PS1" == *"$__phosphor_si_b" ]] || PS1+=$__phosphor_si_b
}

__phosphor_si_preexec() {
    'builtin' 'local' REPLY
    __phosphor_si_encode "$1"
    'builtin' 'print' -nr -- $'\e]133;C;cmdline_url='"$REPLY"$'\a'
}

add-zsh-hook precmd __phosphor_si_boot
