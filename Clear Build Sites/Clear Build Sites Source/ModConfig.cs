namespace ThaleTheGreat.ClearBuildSites;

internal sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public bool ClearStones { get; set; } = true;
    public bool ClearTwigs { get; set; } = true;
    public bool ClearWeeds { get; set; } = true;
    public bool ClearGrass { get; set; } = true;
    public bool ClearTreeSeedsAndSaplings { get; set; } = true;
    public bool ClearWildTrees { get; set; } = true;
    public bool ClearResourceClumps { get; set; } = true;
    public bool ClearFlooringAndPaths { get; set; } = false;
}
