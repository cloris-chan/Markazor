namespace Markazor.Client;

public sealed record MarkazorSettingsAsset(string RepositoryPath, string ContentType, ReadOnlyMemory<byte> Content);
