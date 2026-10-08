using Needle.Models;
using NUnit.Framework;

namespace Needle.Core.Tests;

/// <summary>
///     The displayed line is split into the text before the match, the match and the text after it.
/// </summary>
[TestFixture]
public class MatchLineDisplayTests
{
    private const int Max = MatchLine.MaxDisplayLength;
    private const int Context = MatchLine.ContextBeforeMatch;

    [Test]
    public void Short_line_is_shown_completely()
    {
        var match = Create("var log = new Logger();", "Logger");

        AssertParts(match, "var log = new ", "Logger", "();");
    }

    [Test]
    public void Match_at_start_and_end()
    {
        AssertParts(Create("Logger", "Logger"), "", "Logger", "");
    }

    [Test]
    public void Second_match_in_same_line_is_highlighted()
    {
        var match = new MatchLine { Text = "foo foo", StartIndex = 4, Length = 3 };

        AssertParts(match, "foo ", "foo", "");
    }

    [Test]
    public void Long_line_is_cut_around_the_match()
    {
        var text = new string('a', 100) + "Logger" + new string('b', 200);

        var match = Create(text, "Logger");

        var expectedAfter = new string('b', Max - Context - "Logger".Length);
        AssertParts(match, "…" + new string('a', Context), "Logger", expectedAfter + "…");
        Assert.That((match.DisplayBefore + match.DisplayMatch + match.DisplayAfter).Length, Is.EqualTo(Max + 2));
    }

    [Test]
    public void Long_line_with_match_at_start_is_cut_at_end()
    {
        var text = "Logger" + new string('b', 200);

        var match = Create(text, "Logger");

        AssertParts(match, "", "Logger", new string('b', Max - "Logger".Length) + "…");
    }

    [Test]
    public void Long_line_with_match_at_end_is_cut_at_start()
    {
        var text = new string('a', 200) + "Logger";

        var match = Create(text, "Logger");

        AssertParts(match, "…" + new string('a', Context), "Logger", "");
    }

    [Test]
    public void Very_long_match_is_cut()
    {
        var text = "x" + new string('m', 300) + "y";
        var match = new MatchLine { Text = text, StartIndex = 1, Length = 300 };

        AssertParts(match, "x", new string('m', Max) + "…", "");
    }

    [Test]
    public void Empty_match_is_possible()
    {
        // E.g. the regex "^"
        var match = new MatchLine { Text = "abc", StartIndex = 0, Length = 0 };

        AssertParts(match, "", "", "abc");
    }

    private static MatchLine Create(string text, string matched)
    {
        return new MatchLine { Text = text, StartIndex = text.IndexOf(matched), Length = matched.Length };
    }

    private static void AssertParts(MatchLine match, string before, string matched, string after)
    {
        Assert.That(match.DisplayBefore, Is.EqualTo(before));
        Assert.That(match.DisplayMatch, Is.EqualTo(matched));
        Assert.That(match.DisplayAfter, Is.EqualTo(after));
    }
}
