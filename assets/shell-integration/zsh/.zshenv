# Zinc's shell integration for zsh, part 1 (part 2 is zinc.zsh, next to this file).
#
# Zinc starts zsh with ZDOTDIR pointing at this folder, so zsh reads this file first. It puts
# ZDOTDIR back as it was and loads your .zshenv just as zsh would have; zsh then goes on to your
# .zprofile, .zshrc and .zlogin as usual. In an interactive shell it also loads zinc.zsh,
# which sets itself up at the first prompt (after all of those).

'builtin' 'typeset' __zinc_si_dir=${ZDOTDIR}
if [[ -n "${ZINC_ZDOTDIR+x}" ]]; then
    'builtin' 'export' ZDOTDIR=$ZINC_ZDOTDIR
    'builtin' 'unset' ZINC_ZDOTDIR
else
    'builtin' 'unset' ZDOTDIR
fi

{
    'builtin' 'typeset' __zinc_si_file=${ZDOTDIR-$HOME}/.zshenv
    [[ ! -r "$__zinc_si_file" ]] || 'builtin' 'source' '--' "$__zinc_si_file"
} always {
    if [[ -o 'interactive' && -r "$__zinc_si_dir/zinc.zsh" ]]; then
        'builtin' 'source' '--' "$__zinc_si_dir/zinc.zsh"
    fi
    'builtin' 'unset' __zinc_si_file __zinc_si_dir
}
