using ApiDocs.Domain;

namespace ApiDocs.Application.Ports;

/// <summary>Splits a parsed file into <c>##</c> sections (ARCHITECTURE §6).</summary>
internal interface IChunker
{
    IReadOnlyList<DocumentChunk> Chunk(ParsedFile file);
}
