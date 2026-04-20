namespace DialogEditor.Core.GameData;

public static class GameDataProviderFactory
{
    public static IGameDataProvider? Detect(string rootPath)
    {
        if (Directory.Exists(Path.Combine(rootPath, "PillarsOfEternity_Data")))
            return new Poe1GameDataProvider(rootPath);
        if (Directory.Exists(Path.Combine(rootPath, "PillarsOfEternityII_Data")))
            return new Poe2GameDataProvider(rootPath);
        return null;
    }
}
