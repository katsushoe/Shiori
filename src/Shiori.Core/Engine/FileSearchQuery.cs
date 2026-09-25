namespace Shiori.Core.Engine;

/// <summary>Describes file search conditions. Every supplied condition must match.</summary>
/// <param name="Text">Fragment anywhere in the relative path.</param>
/// <param name="NameStartsWith">Required start of the file name.</param>
/// <param name="NameEndsWith">Required end of the file name.</param>
public sealed record FileSearchQuery(string? Text = null, string? NameStartsWith = null, string? NameEndsWith = null)
{
    /// <summary>Creates a query from optional inputs, treating blank values as absent.</summary>
    /// <exception cref="ArgumentException">Every condition is blank.</exception>
    public static FileSearchQuery Create(string? text, string? nameStartsWith, string? nameEndsWith)
    {
        var query = new FileSearchQuery(Normalize(text), Normalize(nameStartsWith), Normalize(nameEndsWith));
        if (query.Text is null && query.NameStartsWith is null && query.NameEndsWith is null)
        {
            throw new ArgumentException("Specify a query, a file-name prefix, or a file-name suffix.");
        }

        return query;
    }

    /// <summary>Gets the term used to rank file names closer to the request.</summary>
    public string RankingTerm => Text ?? NameStartsWith ?? NameEndsWith ?? string.Empty;

    /// <inheritdoc />
    public override string ToString() =>
        string.Join(
            " ",
            new[]
            {
                Text is null ? null : $"query={Text}",
                NameStartsWith is null ? null : $"nameStartsWith={NameStartsWith}",
                NameEndsWith is null ? null : $"nameEndsWith={NameEndsWith}",
            }.Where(part => part is not null));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
