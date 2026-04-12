using System.Text.Json;
using Connapse.Cli.Models;

namespace Connapse.Cli.Tests.Models;

/// <summary>
/// Regression tests for positional-record DTOs that the CLI deserializes
/// from JSON responses. These guard against a class of trim-related bugs
/// where <see cref="System.Text.Json"/> can't find the parameterized
/// constructor on a trimmed build — the v0.4.0 release shipped with a
/// trimmed binary that blew up on auth login with this exact error:
///
///     Deserialization of types without a parameterless constructor,
///     a singular parameterized constructor, or a parameterized
///     constructor annotated with 'JsonConstructorAttribute' is not
///     supported. Type 'Connapse.Cli.Models.OAuthTokenResponse'.
///
/// v0.4.1 disables trimming, which fixes all of these. These tests pin
/// the deserialization contract so any future attempt to re-enable
/// trimming without addressing the DTO constructors fails loudly.
/// </summary>
public sealed class DeserializationRegressionTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void OAuthTokenResponse_deserializes_from_typical_oauth_body()
    {
        var json = """
            {
              "access_token": "conap_abc123",
              "token_type": "Bearer",
              "expires_in": 3600,
              "refresh_token": "refresh_xyz456",
              "scope": "knowledge:read knowledge:write"
            }
            """;

        var token = JsonSerializer.Deserialize<OAuthTokenResponse>(json, Options);

        Assert.NotNull(token);
        Assert.Equal("conap_abc123", token!.AccessToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(3600, token.ExpiresIn);
        Assert.Equal("refresh_xyz456", token.RefreshToken);
        Assert.Equal("knowledge:read knowledge:write", token.Scope);
    }

    [Fact]
    public void OAuthTokenResponse_deserializes_without_optional_scope()
    {
        var json = """
            {
              "access_token": "conap_abc",
              "token_type": "Bearer",
              "expires_in": 7200,
              "refresh_token": "refresh_xyz"
            }
            """;

        var token = JsonSerializer.Deserialize<OAuthTokenResponse>(json, Options);

        Assert.NotNull(token);
        Assert.Null(token!.Scope);
    }

    [Fact]
    public void MeResponse_deserializes_from_auth_me_body()
    {
        var json = """
            {
              "id": "11111111-2222-3333-4444-555555555555",
              "email": "alice@example.com",
              "displayName": "Alice",
              "roles": ["Owner", "Admin"],
              "createdAt": "2026-01-01T00:00:00Z"
            }
            """;

        var me = JsonSerializer.Deserialize<MeResponse>(json, Options);

        Assert.NotNull(me);
        Assert.Equal("alice@example.com", me!.Email);
        Assert.Equal("Alice", me.DisplayName);
        Assert.Contains("Owner", me.Roles);
    }

    [Fact]
    public void ServerInfo_deserializes_from_api_v1_info_body()
    {
        var json = """
            {
              "edition": "cloud",
              "version": "1.0.0",
              "features": ["organizations", "search"]
            }
            """;

        var info = JsonSerializer.Deserialize<ServerInfo>(json, Options);

        Assert.NotNull(info);
        Assert.Equal("cloud", info!.Edition);
        Assert.Equal("1.0.0", info.Version);
    }

    [Fact]
    public void Organization_deserializes_from_list_response_item()
    {
        var json = """
            {
              "id": "11111111-2222-3333-4444-555555555555",
              "name": "Example Org",
              "slug": "example-org",
              "createdAt": "2026-01-01T00:00:00Z"
            }
            """;

        var org = JsonSerializer.Deserialize<Organization>(json, Options);

        Assert.NotNull(org);
        Assert.Equal("example-org", org!.Slug);
    }
}
