using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Moves focus the way a keyboard user does, by pressing Tab.
///
/// <para><c>Locator.FocusAsync</c> proves a control accepts focus. It does not prove Tab reaches
/// it, and a control that only script can focus is not keyboard reachable. A journey that claims
/// the keyboard is enough walks to its controls with this.</para>
/// </summary>
internal static class KeyboardWalk
{
    /// <summary>
    /// Presses Tab, or Shift+Tab when <paramref name="backwards"/>, until the focused element
    /// matches <paramref name="selector"/>. Fails with the controls it passed on the way.
    /// </summary>
    public static async Task TabToAsync(IPage page, string selector, bool backwards = false, int maxPresses = 120)
    {
        var passed = new List<string>();
        for (var press = 0; press < maxPresses; press++)
        {
            await page.Keyboard.PressAsync(backwards ? "Shift+Tab" : "Tab");
            var focus = await page.EvaluateAsync<string[]>(
                """
                selector => {
                    const element = document.activeElement;
                    if (!element || element === document.body) return ['', '(page)'];
                    const name = element.getAttribute('aria-label') || element.id
                        || (element.textContent || '').trim().slice(0, 30) || element.className;
                    return [element.matches(selector) ? 'match' : '', `${element.tagName.toLowerCase()}[${name}]`];
                }
                """, selector);
            if (focus[0] == "match") return;
            passed.Add(focus[1]);
        }

        throw new Xunit.Sdk.XunitException(
            $"{(backwards ? "Shift+Tab" : "Tab")} did not reach '{selector}' in {maxPresses} presses. "
            + $"Focus passed: {string.Join(" > ", passed)}");
    }

    /// <summary>Presses one key <paramref name="times"/> times.</summary>
    public static async Task PressAsync(IPage page, string key, int times)
    {
        for (var press = 0; press < times; press++) await page.Keyboard.PressAsync(key);
    }
}
