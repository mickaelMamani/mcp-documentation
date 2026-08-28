namespace ApiDocs.Domain;

/// <summary>
/// Type of a chunk, derived from the section heading and from the kind of file it belongs to
/// (DOC-FORMAT §4, ARCHITECTURE §4.1).
/// </summary>
internal enum SectionKind
{
    Description,
    Parameters,
    Response,
    Example,
    Notes,
    DomainOverview,
    DomainUseCases,
    DomainWorkflow,
    DomainAuth,
    DomainErrors,
    Platform,
}
