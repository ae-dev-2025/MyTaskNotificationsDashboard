"""Builds docs/tour.html, a screen-by-screen product tour, from the PNGs in
docs/screenshots/.

    python docs/build-tour.py            # relative <img src>, small file
    python docs/build-tour.py --embed    # inline base64, one shareable file

The screenshots themselves come from a running Windows build driven over CDP;
see docs/README.md for how to recapture them.
"""
import argparse
import base64
import html
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.join(HERE, "screenshots")

# Nav order, which is also the order worth clicking through in a demo.
SCREENS = [
    ("Dashboard", "1-dashboard",
     "What am I meant to be doing right now",
     "Opens on a single answer. The Now panel holds the task in progress with "
     "elapsed and remaining time; Up next is the planner's queue, not a list "
     "anyone ordered by hand.",
     ["Start, Stop and Done act on the task without leaving the page",
      "Tiles total what is left, what is overdue, and what was finished today",
      "Estimate accuracy stays in calibration until five tasks have real spans"]),
    ("Tasks", "2-tasks",
     "The full list, with state visible at a glance",
     "Every task carries an optional deadline, priority and estimate. Priority "
     "reads as a chip, in-progress as a badge, completion as a struck row.",
     ["Filter All, Active or Completed; the footer totals only what remains",
      "Overdue rows flag themselves without needing to be opened",
      "Start on any row makes it the task the dashboard shows under Now"]),
    ("Edit task", "3-edit-modal",
     "One form, shared between add and edit",
     "Add and edit render the same fields component, so the two cannot drift "
     "apart. Not-before constrains the earliest slot the planner may use.",
     ["Split divides an estimate into two to four independent part-tasks",
      "The planner never splits on its own - that stays a decision you make",
      "Everything except the title is optional"]),
    ("Calendar", "4-calendar",
     "What the plan actually looks like against the week",
     "One frame holding the whole day: finished work in green, blocked time as "
     "striped shading, the violet now-line, the task in progress, and the plan "
     "running out ahead of it.",
     ["Unfinished tasks are placed earliest-deadline first, then by priority",
      "Concurrent entries split into side-by-side lanes instead of stacking",
      "A slot that cannot finish in time is tagged late, not just outlined"]),
    ("Blocked time", "5-blocked-time",
     "The hours the planner is not allowed to touch",
     "Recurring weekly windows for sleep and lunch, one-off periods for "
     "appointments. A window may cross midnight.",
     ["The break between tasks is a planner setting, not a per-task field",
      "Blocked periods draw straight onto the calendar as shading",
      "The planner routes around them rather than scheduling over them"]),
]

SPECS = [
    ("Platform", ".NET MAUI Blazor Hybrid"),
    ("Targets", "Windows 10.0.19041 &middot; Android, API 35 and up"),
    ("Storage", "One versioned JSON document, written atomically"),
    ("Theming", "CSS custom properties; follows the OS, with an override"),
    ("Testing", "Playwright and raw CDP driving the running native app"),
]


def src_for(name, embed):
    """Relative path by default; a data URI when the page must stand alone."""
    if not embed:
        return "screenshots/" + name
    with open(os.path.join(SHOTS, name), "rb") as f:
        return "data:image/png;base64," + base64.b64encode(f.read()).decode()


def build_sections(embed):
    out = []
    for i, (nav, stem, tagline, body, points) in enumerate(SCREENS):
        pts = "\n".join(
            "          <li>" + html.escape(p) + "</li>" for p in points)
        out.append(
            '      <section class="screen" id="screen-{n}">\n'
            '        <div class="screen-copy">\n'
            '          <p class="eyebrow">{nav}</p>\n'
            '          <h2>{tag}</h2>\n'
            '          <p class="lede">{body}</p>\n'
            '          <ul class="points">\n{pts}\n          </ul>\n'
            '        </div>\n'
            '        <figure class="shot">\n'
            '          <img class="shot-light" src="{light}" alt="{nav} screen, light theme">\n'
            '          <img class="shot-dark" src="{dark}" alt="{nav} screen, dark theme">\n'
            '        </figure>\n'
            '      </section>'.format(
                n=i + 1, nav=html.escape(nav), tag=html.escape(tagline),
                body=html.escape(body), pts=pts,
                light=src_for(stem + "-light.png", embed),
                dark=src_for(stem + "-dark.png", embed)))
    return "\n".join(out)


SPEC_ROWS = "\n".join(
    '          <div class="spec-row"><dt>{k}</dt><dd>{v}</dd></div>'.format(k=k, v=v)
    for k, v in SPECS)

TEMPLATE = """<title>Task Dashboard &mdash; product tour</title>
<style>
  :root {
    color-scheme: light;
    --ground: #f4f6f8;
    --panel: #ffffff;
    --ink: #1a1f24;
    --ink-soft: #5b666f;
    --ink-faint: #8b959d;
    --rule: #dde3e8;
    --accent: #1b6ec2;
    --marker: #7048e8;
    --on-marker: #ffffff;
    --shadow: 0 1px 2px rgba(16, 24, 32, .08), 0 12px 32px rgba(16, 24, 32, .10);
    --sans: "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif;
    --mono: ui-monospace, "Cascadia Mono", "SF Mono", Consolas, "Liberation Mono", monospace;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      color-scheme: dark;
      --ground: #131719;
      --panel: #1b2024;
      --ink: #e9ecef;
      --ink-soft: #a3adb5;
      --ink-faint: #79838b;
      --rule: #2c3339;
      --accent: #4d9fe8;
      --marker: #9775fa;
      --on-marker: #1a1030;
      --shadow: 0 1px 2px rgba(0, 0, 0, .5), 0 12px 32px rgba(0, 0, 0, .45);
    }
  }
  :root[data-theme="dark"] {
    color-scheme: dark;
    --ground: #131719; --panel: #1b2024; --ink: #e9ecef; --ink-soft: #a3adb5;
    --ink-faint: #79838b; --rule: #2c3339; --accent: #4d9fe8;
    --marker: #9775fa; --on-marker: #1a1030;
    --shadow: 0 1px 2px rgba(0, 0, 0, .5), 0 12px 32px rgba(0, 0, 0, .45);
  }
  :root[data-theme="light"] {
    color-scheme: light;
    --ground: #f4f6f8; --panel: #ffffff; --ink: #1a1f24; --ink-soft: #5b666f;
    --ink-faint: #8b959d; --rule: #dde3e8; --accent: #1b6ec2;
    --marker: #7048e8; --on-marker: #ffffff;
    --shadow: 0 1px 2px rgba(16, 24, 32, .08), 0 12px 32px rgba(16, 24, 32, .10);
  }

  body {
    margin: 0;
    background: var(--ground);
    color: var(--ink);
    font-family: var(--sans);
    line-height: 1.55;
    -webkit-font-smoothing: antialiased;
  }
  .wrap {
    max-width: 1180px;
    margin: 0 auto;
    padding: 0 clamp(1rem, 4vw, 2.5rem) 5rem;
  }

  .masthead {
    display: flex;
    flex-direction: column;
    gap: 1.05rem;
    padding: clamp(2.75rem, 8vw, 5rem) 0 2.25rem;
    border-bottom: 1px solid var(--rule);
  }
  .eyebrow {
    margin: 0;
    font-family: var(--mono);
    font-size: .72rem;
    letter-spacing: .13em;
    text-transform: uppercase;
    color: var(--accent);
  }
  .masthead h1 {
    margin: 0;
    font-size: clamp(2.1rem, 5.2vw, 3.15rem);
    font-weight: 700;
    letter-spacing: -.028em;
    line-height: 1.05;
    text-wrap: balance;
  }
  .masthead p {
    margin: 0;
    max-width: 64ch;
    font-size: 1.05rem;
    color: var(--ink-soft);
  }

  .toolbar {
    position: sticky;
    top: 0;
    z-index: 10;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    margin-bottom: clamp(2rem, 5vw, 3rem);
    padding: .8rem 0;
    background: var(--ground);
    border-bottom: 1px solid var(--rule);
  }
  .toolbar-label {
    font-family: var(--mono);
    font-size: .72rem;
    letter-spacing: .1em;
    text-transform: uppercase;
    color: var(--ink-faint);
  }
  .segmented {
    display: inline-flex;
    padding: 3px;
    gap: 3px;
    background: var(--panel);
    border: 1px solid var(--rule);
    border-radius: 999px;
  }
  .segmented button {
    appearance: none;
    border: 0;
    border-radius: 999px;
    padding: .34rem 1.05rem;
    font: inherit;
    font-size: .84rem;
    font-weight: 600;
    color: var(--ink-soft);
    background: transparent;
    cursor: pointer;
    transition: background 140ms ease, color 140ms ease;
  }
  .segmented button:hover { color: var(--ink); }
  .segmented button[aria-pressed="true"] {
    background: var(--marker);
    color: var(--on-marker);
  }
  .segmented button:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
  }

  .screens {
    display: flex;
    flex-direction: column;
    gap: clamp(3rem, 7vw, 5rem);
  }
  .screen { display: grid; gap: 1.4rem; }
  .screen-copy {
    display: flex;
    flex-direction: column;
    gap: .65rem;
    max-width: 68ch;
  }
  .screen h2 {
    margin: 0;
    font-size: clamp(1.3rem, 2.7vw, 1.68rem);
    font-weight: 650;
    letter-spacing: -.018em;
    line-height: 1.2;
    text-wrap: balance;
  }
  .lede { margin: 0; color: var(--ink-soft); max-width: 62ch; }
  .points {
    margin: .3rem 0 0;
    padding: 0;
    list-style: none;
    display: flex;
    flex-direction: column;
    gap: .38rem;
    font-size: .92rem;
    color: var(--ink-soft);
  }
  .points li { position: relative; padding-left: 1.1rem; }
  .points li::before {
    content: "";
    position: absolute;
    left: 0;
    top: .62em;
    width: .4rem;
    height: .4rem;
    border-radius: 50%;
    background: var(--accent);
  }

  .shot {
    margin: 0;
    border: 1px solid var(--rule);
    border-radius: 12px;
    overflow: hidden;
    background: var(--panel);
    box-shadow: var(--shadow);
  }
  .shot img { display: block; width: 100%; height: auto; }
  .shot .shot-dark { display: none; }
  body[data-shots="dark"] .shot .shot-light { display: none; }
  body[data-shots="dark"] .shot .shot-dark { display: block; }

  .specs {
    margin-top: clamp(3.5rem, 8vw, 5rem);
    padding-top: 1.9rem;
    border-top: 1px solid var(--rule);
  }
  .specs h2 {
    margin: 0 0 1rem;
    font-family: var(--mono);
    font-size: .72rem;
    letter-spacing: .13em;
    text-transform: uppercase;
    color: var(--ink-faint);
    font-weight: 600;
  }
  .spec-list { margin: 0; }
  .spec-row {
    display: grid;
    grid-template-columns: minmax(6.5rem, 11rem) 1fr;
    gap: 1rem;
    padding: .58rem 0;
    border-bottom: 1px solid var(--rule);
  }
  .spec-row dt {
    font-family: var(--mono);
    font-size: .76rem;
    letter-spacing: .04em;
    text-transform: uppercase;
    color: var(--ink-faint);
  }
  .spec-row dd { margin: 0; font-size: .94rem; color: var(--ink-soft); }

  @media (min-width: 900px) {
    .screen {
      grid-template-columns: 19rem 1fr;
      gap: 2.4rem;
      align-items: start;
    }
    .screen-copy { position: sticky; top: 5.25rem; }
  }
  @media (prefers-reduced-motion: reduce) {
    * { transition: none !important; }
  }
</style>

<div class="wrap">
  <header class="masthead">
    <p class="eyebrow">Product tour</p>
    <h1>Task Dashboard</h1>
    <p>A life dashboard that answers three questions: what am I meant to be doing, what will I do next, and what did I actually do. Built as a .NET MAUI Blazor Hybrid app for Windows and Android, with a planner that turns a task list into a timetable.</p>
  </header>

  <div class="toolbar">
    <span class="toolbar-label">Screenshots</span>
    <div class="segmented" role="group" aria-label="Screenshot theme">
      <button type="button" data-shots-set="light" aria-pressed="true">Light</button>
      <button type="button" data-shots-set="dark" aria-pressed="false">Dark</button>
    </div>
  </div>

  <main class="screens">
__SECTIONS__
  </main>

  <section class="specs">
    <h2>Build</h2>
    <dl class="spec-list">
__SPECS__
    </dl>
  </section>
</div>

<script>
  (function () {
    var body = document.body;
    var buttons = Array.prototype.slice.call(document.querySelectorAll('[data-shots-set]'));

    function apply(mode) {
      body.setAttribute('data-shots', mode);
      buttons.forEach(function (b) {
        b.setAttribute('aria-pressed', String(b.getAttribute('data-shots-set') === mode));
      });
    }

    buttons.forEach(function (b) {
      b.addEventListener('click', function () {
        apply(b.getAttribute('data-shots-set'));
      });
    });

    // Open on whichever theme the reader is already viewing the page in.
    var forced = document.documentElement.getAttribute('data-theme');
    var prefersDark = window.matchMedia
      && window.matchMedia('(prefers-color-scheme: dark)').matches;
    apply(forced === 'dark' || (!forced && prefersDark) ? 'dark' : 'light');
  })();
</script>
"""


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--embed", action="store_true",
                    help="inline the PNGs as data URIs, for a single shareable file")
    ap.add_argument("-o", "--out", default=None,
                    help="output path (default docs/tour.html, or docs/tour.embed.html with --embed)")
    args = ap.parse_args()

    missing = [
        stem + suffix
        for _, stem, _, _, _ in SCREENS
        for suffix in ("-light.png", "-dark.png")
        if not os.path.exists(os.path.join(SHOTS, stem + suffix))
    ]
    if missing:
        raise SystemExit("missing screenshots in docs/screenshots/: " + ", ".join(missing))

    page = (TEMPLATE
            .replace("__SECTIONS__", build_sections(args.embed))
            .replace("__SPECS__", SPEC_ROWS))

    out = args.out or os.path.join(
        HERE, "tour.embed.html" if args.embed else "tour.html")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(page)
    print("wrote %s (%.0f KB)" % (os.path.relpath(out), os.path.getsize(out) / 1024))


if __name__ == "__main__":
    main()
