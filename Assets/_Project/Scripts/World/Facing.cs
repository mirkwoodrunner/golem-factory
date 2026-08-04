namespace GolemFactory.World
{
    // Which way a golem is bolted down. Per docs/digital-design.md "Grid & Movement
    // Mechanics": a golem is fixed to a tile and faces one of four directions, pulling from
    // the tile behind it and pushing to the tile in front. Golems cannot pivot at runtime --
    // facing is chosen at placement time and is the spatial expression of that rigidity rule.
    //
    // Deliberately declared in World/ (Runtime asmdef), not Simulation/: the accompanying
    // FacingUtility works in Vector2Int, and GolemFactory.Simulation is noEngineReferences.
    // Ordered clockwise starting at North so RotateClockwise is a plain +1 mod 4.
    public enum Facing
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }
}
