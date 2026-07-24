using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ThaleTheGreat.SurfingFestival.Framework
{
    public sealed class RaceInputMessage
    {
        public const string Type = nameof(RaceInputMessage);
        public string SessionId { get; set; } = string.Empty;
        public int FacingDirection { get; set; }
        public bool UseItem { get; set; }
    }

    public sealed class RaceSnapshotMessage
    {
        public const string Type = nameof(RaceSnapshotMessage);
        public string SessionId { get; set; } = string.Empty;
        public long Sequence { get; set; }
        public bool RaceActive { get; set; }
        public string? Winner { get; set; }
        public List<string> Racers { get; set; } = new();
        public List<RacerSnapshot> RacerStates { get; set; } = new();
        public List<ObstacleSnapshot> Obstacles { get; set; } = new();
    }

    public sealed class RacerSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public Vector2 Position { get; set; }
        public int FacingDirection { get; set; }
        public int Speed { get; set; }
        public int AddedSpeed { get; set; }
        public int Surfboard { get; set; }
        public int RaceFacing { get; set; }
        public int LapsDone { get; set; }
        public bool ReachedHalf { get; set; }
        public SurfItem? CurrentItem { get; set; }
        public int ItemObtainTimer { get; set; }
        public int ItemUsageTimer { get; set; }
        public int SlowdownTimer { get; set; }
        public int StunTimer { get; set; }
    }

    public sealed class ObstacleSnapshot
    {
        public int Id { get; set; }
        public ObstacleType Type { get; set; }
        public Vector2 Position { get; set; }
        public string HomingTarget { get; set; } = string.Empty;
    }
}
