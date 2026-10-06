# Zinc's start for bash run directly (Git Bash, MSYS2's bash.exe).
#
# Zinc starts that bash in POSIX mode with ENV pointing at this file: in POSIX mode, an interactive bash reads only
# the file named by $ENV at startup, and none of its usual ones. So this file turns POSIX mode back off, reads your
# startup files the way bash itself would have (the rules under INVOCATION in bash(1)), and then loads zinc.bash,
# after all of them. That order is the point: a startup file that sets PROMPT_COMMAND outright
# (PROMPT_COMMAND='history -a') would otherwise drop the marks zinc.bash adds there.
#
# Other bashes (in WSL, or started through a launcher script) get zinc.bash through PROMPT_COMMAND instead, which
# works unless a startup file replaces PROMPT_COMMAND.

if [[ $- != *i* || -z "${ZINC_BASH_INJECT-}" ]]; then
    builtin return 0
fi

# What Zinc passed: the startup flags it took out of the command line (--norc, --noprofile), whether HISTFILE needs
# setting, an --rcfile; and your own ENV, if you had one, for the programs started from here.
__zinc_si_flags=$ZINC_BASH_INJECT
__zinc_si_rcfile=${ZINC_BASH_RCFILE-}
builtin unset ENV ZINC_BASH_INJECT ZINC_BASH_RCFILE
if [[ -n "${ZINC_BASH_ENV-}" ]]; then
    builtin export ENV=$ZINC_BASH_ENV
    builtin unset ZINC_BASH_ENV
fi

# Back to bash's own mode before anything of yours runs (inherit_errexit is the one setting --posix turns on that
# set +o posix leaves).
builtin set +o posix
builtin shopt -u inherit_errexit 2>/dev/null

# History: POSIX mode's file is ~/.sh_history; bash's own is ~/.bash_history. (Read after startup, so in time.)
if [[ "$__zinc_si_flags" == *histfile* ]]; then
    HISTFILE=~/.bash_history
fi

if builtin shopt -q login_shell; then
    # A login shell: /etc/profile, then the first of ~/.bash_profile, ~/.bash_login and ~/.profile.
    if [[ "$__zinc_si_flags" != *--noprofile* ]]; then
        [[ ! -r /etc/profile ]] || builtin source /etc/profile
        for __zinc_si_file in ~/.bash_profile ~/.bash_login ~/.profile; do
            if [[ -r "$__zinc_si_file" ]]; then
                builtin source "$__zinc_si_file"
                break
            fi
        done
    fi
elif [[ "$__zinc_si_flags" != *--norc* ]]; then
    # Interactive, not a login shell: the --rcfile, or the system's bashrc (Git for Windows and MSYS2 build bash with
    # /etc/bash.bashrc) and then ~/.bashrc.
    if [[ -n "$__zinc_si_rcfile" ]]; then
        [[ ! -r "$__zinc_si_rcfile" ]] || builtin source "$__zinc_si_rcfile"
    else
        [[ ! -r /etc/bash.bashrc ]] || builtin source /etc/bash.bashrc
        [[ ! -r ~/.bashrc ]] || builtin source ~/.bashrc
    fi
fi

builtin unset __zinc_si_flags __zinc_si_rcfile __zinc_si_file

# Now the integration, after everything above. (zinc.bash marks the first prompt itself only when it runs from
# PROMPT_COMMAND, at that prompt; here the first prompt is still to come.)
__zinc_si_startup=1
builtin source "${BASH_SOURCE[0]%/*}/zinc.bash"
builtin unset __zinc_si_startup
