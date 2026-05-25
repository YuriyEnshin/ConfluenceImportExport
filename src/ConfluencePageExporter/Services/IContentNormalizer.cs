namespace ConfluencePageExporter.Services;

/// <summary>
/// Normalizes Confluence storage format content for comparison purposes.
/// Different implementations handle edge cases differently — XML-based provides
/// more accurate canonicalization, regex-based avoids System.Xml dependencies.
/// </summary>
public interface IContentNormalizer
{
    /// <summary>
    /// Compares two storage format strings after normalization.
    /// Returns true when the content is semantically equivalent
    /// (ignoring formatting differences like whitespace, attribute order, entities).
    /// </summary>
    bool ContentEquals(string? left, string? right);

    /// <summary>
    /// Returns a normalized form of the content suitable for comparison.
    /// </summary>
    string NormalizeForComparison(string content);
}
