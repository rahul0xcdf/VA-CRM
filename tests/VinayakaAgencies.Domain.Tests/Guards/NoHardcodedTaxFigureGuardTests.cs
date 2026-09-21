using System.Text.RegularExpressions;
using VinayakaAgencies.Domain.Tests.TestSupport;

namespace VinayakaAgencies.Domain.Tests.Guards;

/// <summary>
/// Checkpoint C7.5 — "a grep of <c>src/</c> finds no hardcoded section number or rate"
/// (phase-7-tds-payroll-banking.md §3).
/// </summary>
/// <remarks>
/// <para>
/// <b>This test is the checkpoint, executed.</b> The spec states C7.5 as a grep somebody runs; a
/// grep somebody runs is a grep somebody forgets, and the whole point of §2's rule is that it has
/// to survive every later change. So the grep lives here and runs on every build.
/// </para>
/// <para>
/// <b>Why it is worth this much care.</b> The Income Tax Act 2025 renumbers the 194-series into
/// Sections 392/393/394 from 1 April 2026, and §2 requires that to land as a data update rather
/// than a rewrite. One <c>switch</c> arm on "194C", one constant holding 1%, one threshold written
/// as <c>100_000</c>, and the renumbering becomes a code change under a statutory deadline. The
/// same reasoning covers PF and ESI rates and Professional Tax slabs, which §2 brings under
/// non-negotiable #6 by name.
/// </para>
/// <para>
/// <b>Comments and strings are scanned differently.</b> A doc comment naming 194C is
/// explanation — several of this phase's files explain themselves that way and should — whereas a
/// <em>string literal</em> or a constant holding one is the software knowing a section number.
/// Code is therefore stripped of comments before the section patterns run, so the test catches the
/// thing that matters and does not punish the thing that helps.
/// </para>
/// </remarks>
public class NoHardcodedTaxFigureGuardTests
{
    /// <summary>
    /// Section numbers as they are actually written: "194C", "206C(1H)", "194Q", "206AA", and the
    /// 2025 Act's "392"/"393"/"394" heads.
    /// </summary>
    /// <remarks>
    /// Matched inside string literals only. A bare <c>194</c> in arbitrary code would collide with
    /// every unrelated number, and a guard that cries wolf is a guard that gets suppressed.
    /// </remarks>
    private static readonly Regex SectionNumberInLiteral = new(
        @"""[^""]*\b(19[0-9][A-Z]{0,2}|206[A-Z]{1,2}(\(\d+[A-Z]*\))?|39[234])\b[^""]*""",
        RegexOptions.Compiled);

    /// <summary>
    /// Identifier names that would mean a section number had become part of the code's vocabulary.
    /// </summary>
    private static readonly Regex SectionNamedIdentifier = new(
        @"\b(Section|Sec)?_?(194[A-Z]{0,2}|206[A-Z]{1,2}|392|393|394)\b\s*(=|=>|\()",
        RegexOptions.Compiled);

    private static readonly Regex LineComment = new(@"//.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Files allowed to mention a section number in a string.
    /// </summary>
    /// <remarks>
    /// Deliberately empty, and it should stay that way. It exists so that a future exception is a
    /// visible, reviewed edit to this list rather than a quietly relaxed pattern.
    /// </remarks>
    private static readonly string[] AllowedFiles = [];

    private static string SrcDirectory => Path.Combine(RepoRoot.Find(), "src");

    [Fact]
    public void SourceTree_ContainsNoSectionNumberInAnyStringLiteral()
    {
        var offenders = new List<string>();

        foreach (var (file, code) in SourceFilesWithoutComments())
        {
            foreach (Match match in SectionNumberInLiteral.Matches(code))
            {
                offenders.Add($"{Relative(file)}: {match.Value.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "C7.5 — a section number in a string literal is the software knowing a section, and "
            + "the April 2026 renumbering then becomes a code change. Sections are data rows "
            + $"(ITaxSectionService). Found: {string.Join(" | ", offenders)}");
    }

    [Fact]
    public void SourceTree_ContainsNoIdentifierNamedAfterASection()
    {
        var offenders = new List<string>();

        foreach (var (file, code) in SourceFilesWithoutComments())
        {
            foreach (Match match in SectionNamedIdentifier.Matches(code))
            {
                offenders.Add($"{Relative(file)}: {match.Value.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "C7.5 — a constant, property or method named after a section number is the same defect "
            + $"wearing a different hat. Found: {string.Join(" | ", offenders)}");
    }

    /// <summary>
    /// The arithmetic files must hold no figure at all: every rate, threshold, ceiling and slab
    /// arrives as an argument read from a dated row.
    /// </summary>
    /// <remarks>
    /// Narrower and stricter than the sweeps above, and aimed at the two files where a hardcoded
    /// figure would do the most damage. <c>TdsMath</c> and <c>PayrollMath</c> are where somebody
    /// "just putting the 12% in" would look most natural and be most wrong.
    /// </remarks>
    [Theory]
    [InlineData("VinayakaAgencies.Domain", "Tds", "TdsMath.cs")]
    [InlineData("VinayakaAgencies.Domain", "Payroll", "PayrollMath.cs")]
    public void TheArithmeticFiles_HoldNoRateThresholdOrCeilingOfTheirOwn(
        string project,
        string folder,
        string fileName)
    {
        var path = Path.Combine(SrcDirectory, project, folder, fileName);

        File.Exists(path).ShouldBeTrue($"expected to find {path}");

        var code = StripComments(File.ReadAllText(path));

        // The scale constants are allowed and are not tax figures: 10_000 is the minor-unit scale
        // (ADR-0003) and 100 is the definition of "percent". Anything else numeric and large
        // enough to be a rupee threshold or a percentage has no business here.
        var suspiciousNumbers = Regex.Matches(code, @"\b\d[\d_]{2,}\b")
            .Select(match => match.Value)
            .Where(value => value is not ("10_000" or "10_000L" or "100" or "100L"))
            .Distinct()
            .ToList();

        suspiciousNumbers.ShouldBeEmpty(
            $"{fileName} must hold no rate, threshold, ceiling or slab — every figure arrives as "
            + $"an argument from a data row (§2, non-negotiable #6). Found: "
            + string.Join(", ", suspiciousNumbers));
    }

    /// <summary>
    /// The seed files must add no tax figure either.
    /// </summary>
    /// <remarks>
    /// Seeding "194C at 1%" would satisfy every other guard here — it is a data row, after all —
    /// and still be wrong: a seeded rate is a rate nobody owns when the notification moves it, and
    /// §4a D1 records that the owner names their own sections. The check is that these files
    /// mention ledger and group names and nothing numeric.
    /// </remarks>
    [Theory]
    [InlineData("VinayakaAgencies.Application", "Tds", "TdsSeedData.cs")]
    [InlineData("VinayakaAgencies.Application", "Payroll", "PayrollSeedData.cs")]
    public void TheSeedFiles_SeedNoRateThresholdOrSection(
        string project,
        string folder,
        string fileName)
    {
        var path = Path.Combine(SrcDirectory, project, folder, fileName);

        File.Exists(path).ShouldBeTrue($"expected to find {path}");

        var code = StripComments(File.ReadAllText(path));

        Regex.Matches(code, @"\b\d[\d_]*\b")
            .Select(match => match.Value)
            .ShouldBeEmpty($"{fileName} seeds ledgers, never figures.");
    }

    private static IEnumerable<(string File, string Code)> SourceFilesWithoutComments()
    {
        foreach (var file in Directory.GetFiles(SrcDirectory, "*.cs", SearchOption.AllDirectories))
        {
            // Generated obj/ output is a copy of source that no reviewer edits; scanning it would
            // double every finding and make the message unreadable.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (AllowedFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                continue;
            }

            yield return (file, StripComments(File.ReadAllText(file)));
        }
    }

    /// <summary>
    /// Removes comments so that explanation is not mistaken for implementation.
    /// </summary>
    /// <remarks>
    /// Block comments first: a <c>//</c> inside a <c>/* … */</c> would otherwise leave the block's
    /// tail behind, and the remains would be scanned as if they were code.
    /// </remarks>
    private static string StripComments(string content) =>
        LineComment.Replace(BlockComment.Replace(content, string.Empty), string.Empty);

    private static string Relative(string file) => Path.GetRelativePath(SrcDirectory, file);
}
