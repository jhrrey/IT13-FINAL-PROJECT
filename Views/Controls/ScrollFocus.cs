namespace VetCare.Views.Controls;

/// <summary>
/// Windows gives the first focusable control (a search box, a "Check in" button) focus when the
/// window activates, and every ScrollViewer around it then scrolls to "bring it into view" — which
/// could leave a page opened halfway down or a table scrolled sideways. These pages put their
/// controls near the top anyway, so focus changes don't scroll them.
/// </summary>
public static class ScrollFocus
{
    /// <summary>
    /// For side-panel forms: ignore focus while the panel opens (so it starts at the top), then
    /// scroll to focused fields again, e.g. the first invalid field after "Save".
    /// </summary>
    public static void DontScrollOnFocusWhileOpening(ScrollView scrollView)
    {
        scrollView.HandlerChanged += async (s, e) =>
        {
#if WINDOWS
            if (scrollView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer scroller) return;
            scroller.BringIntoViewOnFocusChange = false;
            await Task.Delay(1500);
            scroller.BringIntoViewOnFocusChange = true;
#else
            await Task.CompletedTask;
#endif
        };
    }

    public static void DontScrollOnFocus(params ScrollView[] scrollViews)
    {
        foreach (var scrollView in scrollViews)
        {
            scrollView.HandlerChanged += (s, e) =>
            {
#if WINDOWS
                if (scrollView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer scroller)
                    scroller.BringIntoViewOnFocusChange = false;
#endif
            };
        }
    }
}
