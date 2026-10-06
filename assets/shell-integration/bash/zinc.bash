# Zinc's shell integration for bash (Git Bash, MSYS2, WSL).
#
# Zinc starts bash with PROMPT_COMMAND set to source this file, so it runs once, at the first
# prompt: after your startup files. It changes nothing you see: it adds invisible marks around
# your prompt and commands (OSC 133: prompt start, where typing starts, command running, finished
# with its exit code) and reports the current folder (OSC 7). Zinc uses them to jump between
# prompts, select a command's output, mark failed commands and open new tabs in the same folder.
#
# Marks someone else already sends (a hand-written integration) are left to them. To turn this
# off: shell-integration = "off" under [terminal] in config.toml (or in one [profile.x]).

# Take the bootstrap back out of PROMPT_COMMAND (your startup files may have added to it), and
# don't hand it on to programs started from here.
__zinc_si_boot='. "$ZINC_SHELL_INTEGRATION_DIR/bash/zinc.bash"'
if [[ "$(builtin declare -p PROMPT_COMMAND 2>/dev/null)" == "declare -a"* ]]; then
    for __zinc_si_i in "${!PROMPT_COMMAND[@]}"; do
        [[ "${PROMPT_COMMAND[__zinc_si_i]}" != "$__zinc_si_boot" ]] || builtin unset 'PROMPT_COMMAND[__zinc_si_i]'
    done
    builtin unset __zinc_si_i
else
    PROMPT_COMMAND=${PROMPT_COMMAND//"$__zinc_si_boot;"/}
    PROMPT_COMMAND=${PROMPT_COMMAND//"$__zinc_si_boot"/}
    # Trim what joined it to the rest (a stray ";" at either end is a syntax error).
    while [[ "$PROMPT_COMMAND" == [[:space:]\;]* ]]; do PROMPT_COMMAND=${PROMPT_COMMAND:1}; done
    while [[ "$PROMPT_COMMAND" == *[[:space:]\;] ]]; do PROMPT_COMMAND=${PROMPT_COMMAND%?}; done
    [[ -n "$PROMPT_COMMAND" ]] || builtin unset PROMPT_COMMAND
fi
builtin export -n PROMPT_COMMAND 2>/dev/null
builtin unset __zinc_si_boot

if [[ -n "${__zinc_si_loaded-}" || $- != *i* ]]; then
    builtin return 0
fi
__zinc_si_loaded=1

# zn, Zinc's command-line tool, is a Windows program: WSL finds it only when Windows' PATH is appended
# (appendWindowsPath), and then as zn.exe. Zinc passes its path in ZINC_ZN; `zn` runs it, unless a zn exists
# (Git Bash and MSYS2 find zn.exe on PATH as zn).
if [[ -n "${ZINC_ZN-}" ]] && ! builtin type -P zn >/dev/null 2>&1 && ! builtin declare -F zn >/dev/null; then
    zn() { "$ZINC_ZN" "$@"; }
fi

# Already there: marks or folder reports your prompt sends itself.
__zinc_si_theirs="$PS0$PS1${PROMPT_COMMAND[*]-}$(builtin declare -f 2>/dev/null)"   # (OSH: declare -f needs names)
__zinc_si_marks=1
__zinc_si_folder=1
[[ "$__zinc_si_theirs" != *'133;'* ]] || __zinc_si_marks=
[[ "$__zinc_si_theirs" != *']7;'* ]] || __zinc_si_folder=
builtin unset __zinc_si_theirs

# The current folder as OSC 7 with a file:// URI (percent-encoded byte by byte), for PS1.
__zinc_si_folder_report() {
    [[ "$PWD" != "${__zinc_si_pwd-}" ]] || return 0
    __zinc_si_pwd=$PWD
    # (Assigned apart: with "builtin" in front, local's arguments are split like any command's.)
    builtin local LC_ALL=C path out= c i
    path=$PWD
    [[ "$path" == /* ]] || path=/$path   # C:/x (MSYS, rarely) → file://host/C:/x
    for (( i = 0; i < ${#path}; i++ )); do
        c=${path:i:1}
        case $c in
            [a-zA-Z0-9/._~-]) out+=$c ;;
            *) builtin printf -v c '%%%02X' "'$c"; out+=$c ;;
        esac
    done
    __zinc_si_osc7='\[\e]7;file://'"${HOSTNAME-}$out"'\a\]'
}

# First in PROMPT_COMMAND: the last command's exit code, before anything else changes it.
# (It returns the same status, so what runs after it in PROMPT_COMMAND still sees it in $?.)
__zinc_si_status() { __zinc_si_last=$?; return "$__zinc_si_last"; }

# OSH (Oils) runs this script too, but never shows PS0: its prompt mark asks Zinc to mark each command's start
# itself, when Enter is pressed (as cmd's does).
__zinc_si_aopts="aid=$$"
[[ -z "${OILS_VERSION-}" ]] || __zinc_si_aopts+=";zinc_c=enter"

__zinc_si_b='\[\e]133;B\a\]'
__zinc_si_ps2='\[\e]133;P;k=s\a\]'
__zinc_si_c=$'\e]133;C\a'
__zinc_si_suffix=

# Last in PROMPT_COMMAND: the command finished (D), a prompt starts (A), and the command runs
# once it's read (C, in PS0). The end of PS1 (kept there even if a prompt theme rebuilds PS1)
# reports the folder, drawn with the rest of the prompt, so it arrives with a title the prompt
# sets, and marks where typing starts (B).
__zinc_si_prompt() {
    builtin local suffix=
    if [[ -n "$__zinc_si_folder" ]]; then
        __zinc_si_folder_report
        suffix+=$__zinc_si_osc7
    fi

    if [[ -n "$__zinc_si_marks" ]]; then
        [[ -z "${__zinc_si_prompted-}" ]] || builtin printf '\e]133;D;%s\a' "${__zinc_si_last-0}"
        __zinc_si_prompted=1
        builtin printf '\e]133;A;%s\a' "$__zinc_si_aopts"
        [[ "${PS0-}" == *"$__zinc_si_c"* ]] || PS0+=$__zinc_si_c
        # The continuation prompt ("> " after a trailing \) is prompt, and typing goes on after it.
        [[ "${PS2-}" == "$__zinc_si_ps2"* ]] || PS2=$__zinc_si_ps2${PS2-}$__zinc_si_b
        suffix+=$__zinc_si_b
    fi

    [[ -z "$__zinc_si_suffix" || "$PS1" != *"$__zinc_si_suffix" ]] || PS1=${PS1%"$__zinc_si_suffix"}
    PS1+=$suffix
    __zinc_si_suffix=$suffix
}

if [[ "$(builtin declare -p PROMPT_COMMAND 2>/dev/null)" == "declare -a"* ]]; then
    PROMPT_COMMAND=(__zinc_si_status "${PROMPT_COMMAND[@]}" __zinc_si_prompt)
else
    PROMPT_COMMAND="__zinc_si_status${PROMPT_COMMAND:+; $PROMPT_COMMAND}; __zinc_si_prompt"
fi

# This first time bash is still running PROMPT_COMMAND as it was: mark this prompt now. (Not when startup.bash
# loads this before the first prompt: PROMPT_COMMAND, as just set, marks it.)
[[ -n "${__zinc_si_startup-}" ]] || __zinc_si_prompt
