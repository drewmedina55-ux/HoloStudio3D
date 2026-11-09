"""HoloStudio3D launcher for disc-probe.

Runs `python -m discprobe <command> ...` exactly as at a terminal, so its verdicts and the
ready-to-copy next commands are printed, but from a window: plain text (no colour codes or
animation) and it never waits for typed input. disc-probe itself is unchanged.
"""
import os
import sys

os.environ.setdefault("DISCPROBE_LANG", "en")
os.environ["DISCPROBE_NO_TUTORIAL"] = "1"   # nobody is at a keyboard to answer the walkthrough
os.environ["DISCPROBE_NO_ANIMATION"] = "1"
os.environ["NO_COLOR"] = "1"
os.environ["TERM"] = ""                       # no ANSI codes on Linux/macOS; Windows pipes have none anyway

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)
    sys.stderr.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)
except (AttributeError, ValueError):
    pass

from discprobe import ui  # noqa: E402

# Treat our output as a terminal (so advice prints) but stdin as not one (so nothing prompts).
_real_isatty = ui._isatty
ui._isatty = lambda stream: stream is sys.stdout or (stream is not sys.stdin and _real_isatty(stream))

from discprobe.__main__ import main  # noqa: E402

if __name__ == "__main__":
    args = sys.argv[1:]
    if args[:1] == ["tool"]:
        # `tool <script.py> ...` runs one of disc-probe's helper scripts (e.g. geo_polyfit.py) the same way.
        import runpy
        here = os.path.dirname(os.path.abspath(__file__))
        script = os.path.join(here, "tools", os.path.basename(args[1]))
        sys.argv = [script, *args[2:]]
        runpy.run_path(script, run_name="__main__")
    else:
        main(args)
