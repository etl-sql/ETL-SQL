using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Accessibility and responsive guardrails, asserted on the shipped pages in a real browser.
///
/// <para>These are the checks that only a browser can make: an accessible name is <em>computed</em>
/// from markup, a title attribute, or content — reading the HTML tells you what an author wrote,
/// not what a screen reader will say. Horizontal overflow depends on layout. Focus order depends on
/// what is actually rendered and visible. Each of the properties below is one a person relying on a
/// keyboard or a screen reader needs in order to use the page at all, and none of them is visible
/// to someone testing with a mouse on a wide monitor.</para>
///
/// <para>Every check reports the offending elements by selector and text, because a bare
/// "3 controls have no accessible name" is a finding nobody can act on.</para>
/// </summary>
[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class PortalAccessibilityTests(PortalBrowserFixture fixture)
{
    /// <summary>A phone-width viewport. Not an edge case — it is how the on-call operator looks.</summary>
    private static readonly ViewportSize Narrow = new() { Width = 390, Height = 844 };

    private static readonly ViewportSize Desktop = new() { Width = 1440, Height = 900 };

    public static TheoryData<string> Pages() =>
    [
        "/index.html",
        "/index.html#governance/overview",
        "/index.html#governance/quarantine",
        "/admin.html",
        "/orchestrator.html",
        "/studio.html",
        "/docs.html",
    ];

    // ── Accessible names ────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task EveryInteractiveControl_HasAnAccessibleName(string path)
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await GoToAsync(session.Page, path);

        var unnamed = await session.Page.EvaluateAsync<string[]>(AccessibleNameProbe);

        Assert.True(unnamed.Length == 0,
            $"{path}: these controls are announced with no name at all, so a screen-reader user is "
            + "told there is a button and nothing about what it does:\n  "
            + string.Join("\n  ", unnamed));
    }

    /// <summary>
    /// Collects visible interactive elements whose computed accessible name is empty. The name
    /// sources checked are the ones the accessibility tree actually uses, in order.
    /// </summary>
    private const string AccessibleNameProbe = """
        () => {
          const visible = el => {
            const style = getComputedStyle(el);
            return el.offsetParent !== null
              && style.visibility !== 'hidden'
              && style.display !== 'none'
              && el.getAttribute('aria-hidden') !== 'true'
              && !el.closest('[aria-hidden="true"]');
          };
          const nameOf = el => {
            const labelledBy = el.getAttribute('aria-labelledby');
            if (labelledBy) {
              const text = labelledBy.split(/\s+/)
                .map(id => document.getElementById(id)?.textContent?.trim() || '')
                .join(' ').trim();
              if (text) return text;
            }
            const ariaLabel = el.getAttribute('aria-label')?.trim();
            if (ariaLabel) return ariaLabel;
            if (el.id) {
              const label = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
              if (label?.textContent?.trim()) return label.textContent.trim();
            }
            if (el.closest('label')?.textContent?.trim()) return el.closest('label').textContent.trim();
            const title = el.getAttribute('title')?.trim();
            if (title) return title;
            if (el.tagName === 'INPUT' && el.type === 'submit' && el.value?.trim()) return el.value.trim();
            // Images inside a control contribute their alt text.
            const alt = [...el.querySelectorAll('img[alt]')].map(i => i.alt.trim()).join(' ').trim();
            if (alt) return alt;
            return (el.textContent || '').trim();
          };
          const describe = el => {
            const id = el.id ? `#${el.id}` : '';
            const cls = el.className && typeof el.className === 'string'
              ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.') : '';
            return `<${el.tagName.toLowerCase()}${id}${cls}>`;
          };
          const selector = 'button, a[href], input:not([type=hidden]), select, textarea, '
            + '[role=button], [role=link], [role=tab], [role=checkbox], [role=switch]';
          return [...document.querySelectorAll(selector)]
            .filter(visible)
            .filter(el => nameOf(el).length === 0)
            .map(describe);
        }
        """;

    // ── Responsive layout ───────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task NarrowViewport_DoesNotScrollHorizontally(string path)
    {
        await using var session = await NewSessionAsync(Narrow);
        await fixture.SignInAsync(session.Page);
        await GoToAsync(session.Page, path);

        var overflow = await session.Page.EvaluateAsync<string[]>(HorizontalOverflowProbe);

        // A page body that scrolls sideways on a phone hides content behind a gesture most users
        // never try. Wide content is fine — it just has to scroll inside its own container.
        Assert.True(overflow.Length == 0,
            $"{path} at {Narrow.Width}px: these elements push the page wider than the viewport:\n  "
            + string.Join("\n  ", overflow));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task DoubledTextSize_DoesNotClipContentOrScrollSideways(string path)
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await GoToAsync(session.Page, path);

        // WCAG 1.4.4 at its practical limit: someone who needs 200% text must still be able to read
        // the page, and reflow is what makes that possible instead of a horizontal scrollbar.
        await session.Page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = "html { font-size: 200% !important; }"
        });
        await session.Page.WaitForTimeoutAsync(250);

        var overflow = await session.Page.EvaluateAsync<string[]>(HorizontalOverflowProbe);
        Assert.True(overflow.Length == 0,
            $"{path} at 200% text: these elements push the page wider than the viewport:\n  "
            + string.Join("\n  ", overflow));
    }

    /// <summary>
    /// Returns elements wider than the viewport that are not inside their own scroll container.
    /// A table that scrolls in an <c>overflow-x:auto</c> wrapper is correct and is not reported.
    /// </summary>
    private const string HorizontalOverflowProbe = """
        () => {
          const limit = document.documentElement.clientWidth;
          if (document.documentElement.scrollWidth <= limit + 1) return [];
          const scrolls = el => {
            for (let p = el.parentElement; p; p = p.parentElement) {
              const overflowX = getComputedStyle(p).overflowX;
              if (overflowX === 'auto' || overflowX === 'scroll' || overflowX === 'hidden') return true;
            }
            return false;
          };
          const describe = el => {
            const id = el.id ? `#${el.id}` : '';
            const cls = el.className && typeof el.className === 'string'
              ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.') : '';
            return `<${el.tagName.toLowerCase()}${id}${cls}> right=${Math.round(el.getBoundingClientRect().right)} limit=${limit}`;
          };
          return [...document.querySelectorAll('body *')]
            .filter(el => {
              const style = getComputedStyle(el);
              if (style.display === 'none' || style.visibility === 'hidden') return false;
              if (style.position === 'fixed') return false;
              return el.getBoundingClientRect().right > limit + 1 && !scrolls(el);
            })
            .slice(0, 10)
            .map(describe);
        }
        """;

    // ── Dialogs ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task ClosedDialogs_AreAbsentFromTheAccessibilityTree(string path)
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await GoToAsync(session.Page, path);

        var reachable = await session.Page.EvaluateAsync<string[]>("""
            () => {
              const overlays = [...document.querySelectorAll('[role=dialog], [role=alertdialog]')];
              return overlays
                .filter(el => {
                  const style = getComputedStyle(el);
                  const visuallyClosed = style.display === 'none' || style.visibility === 'hidden'
                    || !el.classList.contains('open') && el.classList.contains('gov-modal-backdrop');
                  if (!visuallyClosed) return false;
                  // Closed but still exposing focusable content is the failure: a keyboard user
                  // tabs into a dialog they cannot see and cannot get out of.
                  const focusable = el.querySelectorAll(
                    'button, [href], input:not([type=hidden]), select, textarea, [tabindex]:not([tabindex="-1"])');
                  return focusable.length > 0
                    && el.getAttribute('aria-hidden') !== 'true'
                    && !el.hasAttribute('inert')
                    && style.display !== 'none'
                    && style.visibility !== 'hidden';
                })
                .map(el => `#${el.id || '(anonymous)'}`);
            }
            """);

        Assert.True(reachable.Length == 0,
            $"{path}: these closed dialogs are still in the accessibility tree, so a keyboard user "
            + "can tab into a dialog they cannot see:\n  " + string.Join("\n  ", reachable));
    }

    // ── Console hygiene ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task LoadingAPage_RaisesNoConsoleErrorsOrUnhandledRejections(string path)
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await GoToAsync(session.Page, path);
        await session.Page.WaitForTimeoutAsync(500);

        // A console error usually does not stop anything, which is exactly why it survives review.
        // It still means a path on this page is broken and nobody has been forced to look at it.
        Assert.True(session.ConsoleErrors.Count == 0,
            $"{path} logged console errors:\n  " + string.Join("\n  ", session.ConsoleErrors));
        Assert.True(session.PageErrors.Count == 0,
            $"{path} raised unhandled exceptions:\n  " + string.Join("\n  ", session.PageErrors));
    }

    // ── Meaning that is not carried by colour ───────────────────────────────────────────────────

    [Fact]
    public async Task StatusChips_CarryTheirMeaningInText_NotOnlyInColour()
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await GoToAsync(session.Page, "/index.html#governance/overview");

        var colourOnly = await session.Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('.gov-badge, .status-chip, .badge, .chip')]
              .filter(el => getComputedStyle(el).display !== 'none')
              .filter(el => (el.textContent || '').trim().length === 0
                && !el.getAttribute('aria-label')
                && !el.getAttribute('title'))
              .map(el => `<${el.tagName.toLowerCase()}.${(el.className || '').split(/\s+/)[0]}>`);
            """);

        // Someone with a colour-vision deficiency, or reading in forced-contrast mode, gets no
        // information from a chip whose only content is its background.
        Assert.True(colourOnly.Length == 0,
            "These status chips convey their meaning only through colour:\n  "
            + string.Join("\n  ", colourOnly));
    }

    // ── Rendering modes ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task BothColourSchemes_RenderWithoutTransparentOrInvisibleText(string scheme)
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await session.Page.EmulateMediaAsync(new PageEmulateMediaOptions
        {
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light
        });
        await GoToAsync(session.Page, "/index.html#governance/overview");

        var invisible = await session.Page.EvaluateAsync<string[]>("""
            () => {
              const parse = c => (c.match(/[\d.]+/g) || []).map(Number);
              const luminance = ([r, g, b]) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
              return [...document.querySelectorAll('body *')]
                .filter(el => {
                  const style = getComputedStyle(el);
                  if (style.display === 'none' || style.visibility === 'hidden') return false;
                  if (!(el.textContent || '').trim()) return false;
                  if (el.children.length > 0) return false;
                  const fg = parse(style.color);
                  if (fg.length >= 4 && fg[3] === 0) return true;
                  // Composite the background the way the renderer does. A translucent layer such
                  // as rgba(255,255,255,.12) is not the background — it is tinting whatever is
                  // underneath, and treating it as opaque reports white-on-dark as invisible.
                  const layers = [];
                  for (let p = el; p; p = p.parentElement) {
                    const c = parse(getComputedStyle(p).backgroundColor);
                    if (c.length < 3) continue;
                    const alpha = c.length >= 4 ? c[3] : 1;
                    if (alpha === 0) continue;
                    layers.push([c[0], c[1], c[2], alpha]);
                    if (alpha >= 0.99) break;
                  }
                  if (layers.length === 0) return false;
                  // Bottom-most first, then paint each layer over the accumulated result.
                  let bg = layers[layers.length - 1].slice(0, 3);
                  for (let i = layers.length - 2; i >= 0; i--) {
                    const [r, g, b, a] = layers[i];
                    bg = [r * a + bg[0] * (1 - a), g * a + bg[1] * (1 - a), b * a + bg[2] * (1 - a)];
                  }
                  return Math.abs(luminance(fg) - luminance(bg)) < 8;
                })
                .slice(0, 10)
                .map(el => `<${el.tagName.toLowerCase()}.${(el.className || '').toString().split(/\s+/)[0]}> "${(el.textContent || '').trim().slice(0, 40)}"`);
            }
            """);

        // A theme that only ever gets looked at in one mode grows text the same colour as its
        // background — legible to whoever wrote it, invisible to everyone whose OS says otherwise.
        Assert.True(invisible.Length == 0,
            $"In {scheme} mode this text is effectively invisible against its background:\n  "
            + string.Join("\n  ", invisible));
    }

    [Fact]
    public async Task ReducedMotion_IsHonouredByEveryAnimation()
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await session.Page.EmulateMediaAsync(new PageEmulateMediaOptions
        {
            ReducedMotion = ReducedMotion.Reduce
        });
        await GoToAsync(session.Page, "/index.html#governance/overview");

        var animating = await session.Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('body *')]
              .filter(el => {
                const style = getComputedStyle(el);
                if (style.display === 'none' || style.visibility === 'hidden') return false;
                // The standard reduced-motion technique collapses durations to .001ms rather
                // than removing the animation, so animationName stays set and the duration is a
                // non-zero number. Anything under ~50ms is imperceptible and counts as honoured.
                const perceptible = 0.05;
                const animated = style.animationName !== 'none'
                  && parseFloat(style.animationDuration) > perceptible;
                const transitioned = parseFloat(style.transitionDuration) > perceptible;
                return animated || transitioned;
              })
              .slice(0, 10)
              .map(el => {
                const style = getComputedStyle(el);
                return `<${el.tagName.toLowerCase()}.${(el.className || '').toString().split(/\s+/)[0]}> `
                  + `animation=${style.animationName}/${style.animationDuration} transition=${style.transitionDuration}`;
              });
            """);

        // prefers-reduced-motion is set by people for whom motion causes nausea or migraine. It is
        // a medical preference, not a taste, so honouring it is not optional polish.
        Assert.True(animating.Length == 0,
            "These elements still animate with prefers-reduced-motion set:\n  "
            + string.Join("\n  ", animating));
    }

    [Fact]
    public async Task ForcedColours_LeaveNoElementPaintedIntoInvisibility()
    {
        await using var session = await NewSessionAsync(Desktop);
        await fixture.SignInAsync(session.Page);
        await session.Page.EmulateMediaAsync(new PageEmulateMediaOptions
        {
            ForcedColors = ForcedColors.Active
        });
        await GoToAsync(session.Page, "/index.html#governance/overview");

        // In forced-colours mode the OS replaces the palette. Anything that pins its own colours
        // with `forced-color-adjust: none` opts out of that substitution and can end up painting
        // over the user's chosen high-contrast scheme — the one they set in order to see at all.
        var optedOut = await session.Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('body *')]
              .filter(el => getComputedStyle(el).forcedColorAdjust === 'none')
              .slice(0, 10)
              .map(el => `<${el.tagName.toLowerCase()}.${(el.className || '').toString().split(/\s+/)[0]}>`);
            """);

        Assert.True(optedOut.Length == 0,
            "These elements opt out of forced-colours substitution:\n  " + string.Join("\n  ", optedOut));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<BrowserSession> NewSessionAsync(ViewportSize viewport)
    {
        var session = await fixture.NewSessionAsync();
        await session.Page.SetViewportSizeAsync(viewport.Width, viewport.Height);
        return session;
    }

    private static async Task GoToAsync(IPage page, string path)
    {
        await page.GotoAsync(path);
        // A hash route on an already-loaded document does not re-run the entry script.
        if (path.Contains('#')) await page.ReloadAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
            new PageWaitForLoadStateOptions { Timeout = 30_000 });
    }

}
