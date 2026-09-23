namespace ConverterPoC;

// The Crossref content types this tool can deposit
public enum CrossrefContentType
{
    Book,
    JournalArticle,
    Dataset,
    PostedContent
}

public static class CrossrefContentTypes
{
    // By InvenioRDM resource_type.id; types without a closer Crossref equivalent are posted content
    public static CrossrefContentType FromResourceType(string? resourceTypeId) => resourceTypeId switch
    {
        "publication-book" => CrossrefContentType.Book,
        "publication-article" => CrossrefContentType.JournalArticle,
        "dataset" => CrossrefContentType.Dataset,
        _ => CrossrefContentType.PostedContent
    };

    // By the "type" of an already registered DOI in the Crossref REST API. Crossref doesn't let a
    // deposit change a DOI's content type, so updates must keep it; null if this tool can't produce it.
    public static CrossrefContentType? FromRegisteredType(string? crossrefType) => crossrefType switch
    {
        "book" or "monograph" or "edited-book" or "reference-book" => CrossrefContentType.Book,
        "journal-article" => CrossrefContentType.JournalArticle,
        "dataset" => CrossrefContentType.Dataset,
        "posted-content" => CrossrefContentType.PostedContent,
        _ => null
    };
}
