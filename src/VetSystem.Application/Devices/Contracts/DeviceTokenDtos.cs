namespace VetSystem.Application.Devices.Contracts;

/// <summary>
/// M21 — self-scoped push-token registration. The device is identified by the token itself
/// (globally unique); the owner is always the authenticated caller, never a body field.
/// </summary>
public sealed record RegisterPushTokenRequest(string Token, string Platform);

/// <summary>Unregister-by-token (logout). Idempotent: an unknown token is already gone.</summary>
public sealed record UnregisterPushTokenRequest(string Token);
