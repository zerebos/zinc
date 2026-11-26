// The fork and exec behind a POSIX pseudoterminal, in C because a .NET process can't fork safely from managed code.
// Between fork and exec the child calls only async-signal-safe functions.
#include <signal.h>
#include <sys/ioctl.h>
#include <sys/wait.h>
#include <termios.h>
#include <unistd.h>
#include <util.h>

// Starts `file` on a new pseudoterminal and returns its pid, or -1. `master` gets the terminal's side.
int zinc_pty_spawn(const char *file, char *const argv[], char *const envp[], const char *cwd,
                   unsigned short cols, unsigned short rows, int *master) {
    struct winsize size = {rows, cols, 0, 0};
    pid_t pid = forkpty(master, NULL, NULL, &size);
    if (pid != 0) return pid;

    struct termios t;
    if (tcgetattr(0, &t) == 0) {
        t.c_iflag |= IUTF8; // the line discipline erases a whole UTF-8 character on backspace
        tcsetattr(0, TCSANOW, &t);
    }
    if (cwd) chdir(cwd);
    for (int s = 1; s < NSIG; s++) signal(s, SIG_DFL); // .NET ignores SIGPIPE and others, which exec would pass on
    sigset_t none;
    sigemptyset(&none);
    sigprocmask(SIG_SETMASK, &none, NULL);
    execve(file, argv, envp);
    _exit(127);
}

int zinc_pty_resize(int master, unsigned short cols, unsigned short rows) {
    struct winsize size = {rows, cols, 0, 0};
    return ioctl(master, TIOCSWINSZ, &size);
}

// Waits for the child and returns its exit code, or 128 plus the signal that ended it.
int zinc_pty_wait(int pid) {
    int status = 0;
    while (waitpid(pid, &status, 0) < 0) {}
    return WIFEXITED(status) ? WEXITSTATUS(status) : 128 + WTERMSIG(status);
}
