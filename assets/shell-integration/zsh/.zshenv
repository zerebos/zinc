# Phosphor's shell integration for zsh, part 1 (part 2 is phosphor.zsh, next to this file).
#
# Phosphor starts zsh with ZDOTDIR pointing at this folder, so zsh reads this file first. It puts
# ZDOTDIR back as it was and loads your .zshenv just as zsh would have; zsh then goes on to your
# .zprofile, .zshrc and .zlogin as usual. In an interactive shell it also loads phosphor.zsh,
# which sets itself up at the first prompt (after all of those).

'builtin' 'typeset' __phosphor_si_dir=${ZDOTDIR}
if [[ -n "${PHOSPHOR_ZDOTDIR+x}" ]]; then
    'builtin' 'export' ZDOTDIR=$PHOSPHOR_ZDOTDIR
    'builtin' 'unset' PHOSPHOR_ZDOTDIR
else
    'builtin' 'unset' ZDOTDIR
fi

{
    'builtin' 'typeset' __phosphor_si_file=${ZDOTDIR-$HOME}/.zshenv
    [[ ! -r "$__phosphor_si_file" ]] || 'builtin' 'source' '--' "$__phosphor_si_file"
} always {
    if [[ -o 'interactive' && -r "$__phosphor_si_dir/phosphor.zsh" ]]; then
        'builtin' 'source' '--' "$__phosphor_si_dir/phosphor.zsh"
    fi
    'builtin' 'unset' __phosphor_si_file __phosphor_si_dir
}
