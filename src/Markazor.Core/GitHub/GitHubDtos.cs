using System.Text.Json;
using System.Text.Json.Serialization;

namespace Markazor.Core.GitHub;

public sealed class GitHubRepositoryResponse
{
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = string.Empty;

    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; init; } = string.Empty;

    [JsonPropertyName("private")]
    public bool IsPrivate { get; init; }

    public GitHubRepositoryPermissionsResponse? Permissions { get; init; }
}

public sealed class GitHubRepositoryPermissionsResponse
{
    public bool Pull { get; init; }

    public bool Push { get; init; }
}

public sealed class GitHubBlobResponse
{
    public string Sha { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Encoding { get; init; } = string.Empty;
}

public sealed class GitHubCreateBlobRequest
{
    public string Content { get; init; } = string.Empty;

    public string Encoding { get; init; } = "base64";
}

public sealed class GitHubContentFileResponse
{
    public string Path { get; init; } = string.Empty;

    public string Sha { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Encoding { get; init; } = string.Empty;
}

public sealed class GitHubContentsWriteRequest
{
    public string Message { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Branch { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sha { get; init; }
}

public sealed class GitHubContentsDeleteRequest
{
    public string Message { get; init; } = string.Empty;

    public string Sha { get; init; } = string.Empty;

    public string Branch { get; init; } = string.Empty;
}

public sealed class GitHubContentsWriteResponse
{
    public GitHubContentSummaryResponse? Content { get; init; }

    public GitHubCommitSummaryResponse? Commit { get; init; }
}

public sealed class GitHubContentSummaryResponse
{
    public string Path { get; init; } = string.Empty;

    public string Sha { get; init; } = string.Empty;
}

public sealed class GitHubCommitSummaryResponse
{
    public string Sha { get; init; } = string.Empty;
}

public sealed class GitHubGitObjectResponse
{
    public string Sha { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;
}

public sealed class GitHubRefResponse
{
    [JsonPropertyName("ref")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("object")]
    public GitHubGitObjectResponse? Target { get; init; }
}

public sealed class GitHubTreeResponse
{
    public string Sha { get; init; } = string.Empty;

    public IReadOnlyList<GitHubTreeEntryResponse> Tree { get; init; } = [];

    public bool Truncated { get; init; }
}

public sealed class GitHubTreeEntryResponse
{
    public string Path { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Sha { get; init; } = string.Empty;
}

public sealed class GitHubCreateTreeRequest
{
    [JsonPropertyName("base_tree")]
    public string BaseTreeSha { get; init; } = string.Empty;

    public IReadOnlyList<GitHubCreateTreeEntryRequest> Tree { get; init; } = [];
}

[JsonConverter(typeof(GitHubCreateTreeEntryRequestJsonConverter))]
public sealed class GitHubCreateTreeEntryRequest
{
    public string Path { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Content { get; init; }

    public string? Sha { get; init; }

    public bool Delete { get; init; }
}

public sealed class GitHubCreateCommitRequest
{
    public string Message { get; init; } = string.Empty;

    public string Tree { get; init; } = string.Empty;

    public IReadOnlyList<string> Parents { get; init; } = [];
}

public sealed class GitHubCreateCommitResponse
{
    public string Sha { get; init; } = string.Empty;
}

public sealed class GitHubCommitResponse
{
    public string Sha { get; init; } = string.Empty;

    public GitHubGitObjectResponse? Tree { get; init; }
}

public sealed class GitHubUpdateRefRequest
{
    public string Sha { get; init; } = string.Empty;

    public bool Force { get; init; }
}

internal sealed class GitHubCreateTreeEntryRequestJsonConverter : JsonConverter<GitHubCreateTreeEntryRequest>
{
    public override GitHubCreateTreeEntryRequest? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException("GitHub tree entries are only serialized by Markazor.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        GitHubCreateTreeEntryRequest value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WriteString("mode", value.Mode);
        writer.WriteString("type", value.Type);

        if (value.Delete)
        {
            writer.WriteNull("sha");
        }
        else if (!string.IsNullOrWhiteSpace(value.Sha))
        {
            writer.WriteString("sha", value.Sha);
        }
        else
        {
            writer.WriteString("content", value.Content);
        }

        writer.WriteEndObject();
    }
}
