namespace VetSystem.Application.Identity;

/// <summary>
/// Mints a unique <c>users.number_prefix</c> per environment on approval. Used downstream by
/// <c>visit_number</c> and <c>invoice.number</c> client-side generators so offline-created records
/// on different devices never collide (SCHEMA "Key invariants" #9).
/// </summary>
public interface INumberPrefixGenerator
{
    Task<string> GenerateUniqueAsync(Guid environmentId, CancellationToken cancellationToken);
}
