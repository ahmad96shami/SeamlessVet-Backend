namespace VetSystem.Domain.Common;

/// <summary>
/// Abstracts Guid v7 generation so production code can pick the platform path
/// (<see cref="System.Guid.CreateVersion7()"/>) while tests can supply a deterministic source.
/// PG18 also exposes a server-side <c>uuidv7()</c>; this generator is the app-side fallback used
/// when the client did not supply an id.
/// </summary>
public interface IGuidV7Generator
{
    Guid New();
}

public sealed class GuidV7Generator : IGuidV7Generator
{
    public Guid New() => Guid.CreateVersion7();
}
