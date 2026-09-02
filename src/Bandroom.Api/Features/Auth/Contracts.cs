namespace Bandroom.Api.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record TokenPairResponse(string AccessToken, int ExpiresInSeconds, string RefreshToken);

public sealed record MeResponse(Guid Id, string Email, string DisplayName);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
