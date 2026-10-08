using Needle.Models;
using NUnit.Framework;

namespace Needle.Core.Tests;

[TestFixture]
public class LineWindowTests
{
    [Test]
    public void Short_line_is_not_cut()
    {
        var text = new string('x', 100);

        var window = LineWindow.Create(text, [Match(50, 3)], 100);

        Assert.That(window, Is.EqualTo(new LineWindow(0, 100, 0)));
    }

    [Test]
    public void Line_is_not_cut_without_max_columns()
    {
        var text = new string('x', 10_000);

        var window = LineWindow.Create(text, [Match(5_000, 3)], 0);

        Assert.That(window, Is.EqualTo(new LineWindow(0, 10_000, 0)));
    }

    [Test]
    public void Long_line_starts_with_context_before_the_first_match()
    {
        var text = new string('x', 1000);

        var window = LineWindow.Create(text, [Match(500, 3)], 200);

        Assert.That(window, Is.EqualTo(new LineWindow(460, 660, 0)));
    }

    [Test]
    public void Context_is_limited_to_a_quarter_of_the_columns()
    {
        var text = new string('x', 1000);

        var window = LineWindow.Create(text, [Match(500, 3)], 40);

        Assert.That(window, Is.EqualTo(new LineWindow(490, 530, 0)));
    }

    [Test]
    public void Match_at_the_start_is_shown_from_the_start()
    {
        var text = new string('x', 1000);

        var window = LineWindow.Create(text, [Match(10, 3)], 200);

        Assert.That(window, Is.EqualTo(new LineWindow(0, 200, 0)));
    }

    [Test]
    public void Match_at_the_end_gets_more_context()
    {
        var text = new string('x', 1000);

        var window = LineWindow.Create(text, [Match(990, 3)], 200);

        Assert.That(window, Is.EqualTo(new LineWindow(800, 1000, 0)));
    }

    [Test]
    public void Matches_after_the_window_are_counted()
    {
        var text = new string('x', 1000);

        var window = LineWindow.Create(text, [Match(100, 3), Match(150, 3), Match(400, 3), Match(900, 3)], 200);

        Assert.That(window, Is.EqualTo(new LineWindow(60, 260, 2)));
    }

    [Test]
    public void Surrogate_pairs_are_not_split()
    {
        // Each emoji is a surrogate pair. Without adjusting, the window would start and end inside one.
        var text = string.Concat(Enumerable.Repeat("😀", 500));

        var window = LineWindow.Create(text, [Match(502, 2)], 100);

        Assert.That(char.IsLowSurrogate(text[window.Start]), Is.False);
        Assert.That(char.IsLowSurrogate(text[window.End]), Is.False);
    }

    private static MatchLine Match(int startIndex, int length)
    {
        return new MatchLine { StartIndex = startIndex, Length = length };
    }
}
